using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace LaunchDeck {
static class Updater {
    public const string Version="1.1.1";
    const string Api="https://api.github.com/repos/rubricalchip134/LaunchDeck/releases/latest";
    public static bool IsNewer(string tag) {
        System.Version current, latest;
        return System.Version.TryParse(Version,out current) && System.Version.TryParse((tag??"").Trim().TrimStart('v','V'),out latest) && latest>current;
    }
    static string Hash(string file) { using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
    public static void Check(Form owner, Action<string> status, bool userRequested) {
        status("Checking for LaunchDeck updates...");
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;
                string json;
                using(var client=new WebClient()){client.Headers.Add("User-Agent","LaunchDeck/"+Version);json=client.DownloadString(Api);}
                var release=new JavaScriptSerializer().DeserializeObject(json) as System.Collections.Generic.Dictionary<string,object>;
                string tag=release==null?null:release["tag_name"] as string;
                if(!IsNewer(tag)){owner.BeginInvoke((Action)(()=>status(userRequested?"You're using the latest version (v"+Version+").":"LaunchDeck is up to date.")));return;}
                string exeUrl=null,hashUrl=null;
                var assets=release["assets"] as object[];
                if(assets!=null)foreach(object item in assets){var asset=item as System.Collections.Generic.Dictionary<string,object>;if(asset==null)continue;string name=asset["name"] as string,url=asset["browser_download_url"] as string;if(string.Equals(name,"LaunchDeck.exe",StringComparison.OrdinalIgnoreCase))exeUrl=url;if(string.Equals(name,"LaunchDeck.exe.sha256",StringComparison.OrdinalIgnoreCase))hashUrl=url;}
                if(string.IsNullOrWhiteSpace(exeUrl)||string.IsNullOrWhiteSpace(hashUrl))throw new IOException("The release is missing its signed-off update files.");
                string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LaunchDeck","Updates");Directory.CreateDirectory(dir);
                string next=Path.Combine(dir,"LaunchDeck-"+tag.TrimStart('v','V')+".exe"),hashFile=next+".sha256";
                using(var client=new WebClient()){client.Headers.Add("User-Agent","LaunchDeck/"+Version);client.DownloadFile(exeUrl,next);client.DownloadFile(hashUrl,hashFile);}
                string expected=File.ReadAllText(hashFile).Trim().Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
                if(expected.Length!=64||Hash(next)!=expected){File.Delete(next);throw new IOException("The downloaded update did not pass its security check.");}
                owner.BeginInvoke((Action)(()=>{status("LaunchDeck "+tag+" is ready.");if(MessageBox.Show(owner,"LaunchDeck "+tag+" is available.\n\nUpdate now? Your deck will stay saved.","Update ready",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes){Process.Start(new ProcessStartInfo(next,"--apply-update \""+Application.ExecutablePath+"\" "+Process.GetCurrentProcess().Id){UseShellExecute=true});owner.Close();}}));
            } catch(Exception ex) { owner.BeginInvoke((Action)(()=>status(userRequested?"Update check failed: "+ex.Message:"Could not check for updates. Use Check for updates to retry."))); }
        });
    }
    public static int Apply(string target, int pid) {
        try { try{Process.GetProcessById(pid).WaitForExit(30000);}catch(ArgumentException){} File.Copy(Application.ExecutablePath,target,true);Process.Start(new ProcessStartInfo(target){UseShellExecute=true});return 0; }
        catch(Exception ex){MessageBox.Show("LaunchDeck could not finish updating.\n\n"+ex.Message+"\n\nMove LaunchDeck.exe to a folder you can write to and try again.","Update failed");return 1;}
    }
}
public class Entry {
    public string Name, Path;
    public Entry(string name, string path) { Name = name; Path = path; }
}
public class InstalledApp {
    public string Name, Id; public int Score;
    public InstalledApp(string name,string id){Name=name;Id=id;Score=Recommend(name);}
    static int Recommend(string name){string n=(name??"").ToLowerInvariant();string[] picks={"edge","chrome","firefox","spotify","discord","teams","slack","zoom","steam","xbox","obs","vlc","photos","calculator","notepad","visual studio code","paint","terminal","powertoys"};for(int i=0;i<picks.Length;i++)if(n.Contains(picks[i]))return 100-i;return 0;}
}
static class AppCatalog {
    public static InstalledApp[] Load(){
        var list=new System.Collections.Generic.List<InstalledApp>();
        var seen=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var psi=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Get-StartApps | Sort-Object Name | ForEach-Object { [Console]::WriteLine(($_.Name -replace '[\\r\\n\\t]',' ') + [char]9 + $_.AppID) }\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var p=Process.Start(psi)){if(p==null)throw new IOException("Windows app search could not start.");string line;while((line=p.StandardOutput.ReadLine())!=null){int tab=line.IndexOf('\t');if(tab<1)continue;string name=line.Substring(0,tab).Trim(),id=line.Substring(tab+1).Trim();if(name.Length>0&&id.Length>0&&seen.Add(id))list.Add(new InstalledApp(name,id));}if(!p.WaitForExit(20000)){try{p.Kill();}catch{}throw new IOException("Windows app search took too long.");}if(p.ExitCode!=0)throw new IOException("Windows could not list installed apps.");}
        return list.OrderByDescending(x=>x.Score).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
public class DeckStore {
    public Entry[] Items = new Entry[15];
    public string FileName;
    public DeckStore(string file) { FileName = file; }
    public void Load() {
        if (!File.Exists(FileName)) return;
        var doc = XDocument.Load(FileName);
        if (doc.Root == null || doc.Root.Name != "deck") throw new IOException("The saved deck is not valid.");
        foreach (var e in doc.Root.Elements("app")) {
            int i; if (!int.TryParse((string)e.Attribute("slot"), out i) || i < 0 || i >= Items.Length) continue;
            string path = (string)e.Attribute("path");
            if (!string.IsNullOrWhiteSpace(path)) Items[i] = new Entry((string)e.Attribute("name") ?? "App", path);
        }
    }
    public void Save() {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FileName));
        var root = new XElement("deck", new XAttribute("version", 1));
        for (int i=0; i<Items.Length; i++) if (Items[i]!=null)
            root.Add(new XElement("app", new XAttribute("slot",i), new XAttribute("name",Items[i].Name), new XAttribute("path",Items[i].Path)));
        string temp=FileName+".tmp";
        new XDocument(root).Save(temp);
        if (File.Exists(FileName)) File.Replace(temp,FileName,FileName+".bak"); else File.Move(temp,FileName);
    }
    public void Swap(int from, int to) { var old=Items[to]; Items[to]=Items[from]; Items[from]=old; }
    public int AddFiles(string[] files, int start) {
        int added=0;
        foreach (string file in files) {
            if (!File.Exists(file) && !Directory.Exists(file)) continue;
            int target=-1;
            for (int n=0;n<Items.Length;n++) { int i=(start+n)%Items.Length; if(Items[i]==null) {target=i;break;} }
            if (target<0) break;
            Items[target]=new Entry(System.IO.Path.GetFileNameWithoutExtension(file),file); added++;
        }
        return added;
    }
}
static class Theme {
    public static Color Background=Color.FromArgb(17,20,25), Card=Color.FromArgb(29,34,41), Border=Color.FromArgb(49,57,66), Ink=Color.FromArgb(239,243,246), Muted=Color.FromArgb(157,170,182), Accent=Color.FromArgb(176,241,118);
    public static GraphicsPath Round(Rectangle r, int radius) {
        int d=radius*2; var p=new GraphicsPath(); p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
    public static Button Button(string text) { return new Button { Text=text, FlatStyle=FlatStyle.Flat, BackColor=Card, ForeColor=Ink, Height=38, Cursor=Cursors.Hand, Font=new Font("Segoe UI",10), AutoSize=false }; }
}
class Tile : Button {
    public int Slot; public Entry Entry; public Image AppIcon;
    bool hover, drop, dragged; Point down;
    public Action<int,int> MoveTile; public Action<int,string[]> DropFiles;
    public Tile(int slot) {
        Slot=slot; AllowDrop=true; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0;
        BackColor=Theme.Background; Cursor=Cursors.Hand;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        MouseEnter+=(s,e)=>{hover=true;Invalidate();}; MouseLeave+=(s,e)=>{hover=false;Invalidate();};
        MouseDown+=(s,e)=>{down=e.Location;dragged=false;};
        MouseMove+=(s,e)=>{ if(e.Button==MouseButtons.Left && Entry!=null && !dragged && (Math.Abs(e.X-down.X)>SystemInformation.DragSize.Width || Math.Abs(e.Y-down.Y)>SystemInformation.DragSize.Height)) { dragged=true; DoDragDrop(new DataObject("LaunchDeckSlot",Slot),DragDropEffects.Move); } };
        DragEnter+=(s,e)=>{ e.Effect=e.Data.GetDataPresent("LaunchDeckSlot") ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; drop=e.Effect!=DragDropEffects.None;Invalidate(); };
        DragLeave+=(s,e)=>{drop=false;Invalidate();};
        DragDrop+=(s,e)=>{drop=false;Invalidate(); if(e.Data.GetDataPresent("LaunchDeckSlot")) MoveTile((int)e.Data.GetData("LaunchDeckSlot"),Slot); else if(e.Data.GetDataPresent(DataFormats.FileDrop)) DropFiles(Slot,(string[])e.Data.GetData(DataFormats.FileDrop));};
    }
    protected override void OnClick(EventArgs e) { if(!dragged) base.OnClick(e); dragged=false; }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new Rectangle(2,2,Width-5,Height-5);
        using(var p=Theme.Round(r,14)) using(var b=new SolidBrush(hover||drop?Color.FromArgb(38,47,53):Theme.Card)) using(var pen=new Pen(drop||Focused?Theme.Accent:Theme.Border,drop||Focused?2:1)) {g.FillPath(b,p);g.DrawPath(pen,p);}
        using(var f=new Font("Segoe UI",9)) TextRenderer.DrawText(g,(Slot+1).ToString("00"),f,new Point(14,12),Theme.Muted);
        if(Entry==null) {
            using(var pen=new Pen(hover?Theme.Accent:Theme.Muted,2)) {int x=Width/2,y=Height/2-10;g.DrawLine(pen,x-10,y,x+10,y);g.DrawLine(pen,x,y-10,x,y+10);}
            using(var f=new Font("Segoe UI",10)) TextRenderer.DrawText(g,"Drop an app",f,new Rectangle(8,Height-44,Width-16,26),Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        } else {
            int size=Math.Min(48,Height-75);
            if(AppIcon!=null) g.DrawImage(AppIcon,(Width-size)/2,Math.Max(25,(Height-size)/2-12),size,size);
            using(var f=new Font("Segoe UI Semibold",11)) TextRenderer.DrawText(g,Entry.Name,f,new Rectangle(10,Height-43,Width-20,28),Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        }
    }
    protected override void Dispose(bool disposing) {if(disposing && AppIcon!=null)AppIcon.Dispose();base.Dispose(disposing);}
}
class EditDialog : Form {
    public TextBox NameBox=new TextBox(), PathBox=new TextBox();
    public EditDialog(Entry entry) {
        Text=entry==null?"Add app":"Edit app"; ClientSize=new Size(490,232); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;BackColor=Theme.Background;ForeColor=Theme.Ink;Font=new Font("Segoe UI",10);
        Controls.Add(new Label{Text="App name",Location=new Point(20,17),AutoSize=true}); NameBox.SetBounds(20,43,450,28); Controls.Add(NameBox);
        Controls.Add(new Label{Text="App or shortcut",Location=new Point(20,85),AutoSize=true}); PathBox.SetBounds(20,110,350,28);Controls.Add(PathBox);
        var browse=Theme.Button("Browse...");browse.SetBounds(380,107,90,32);Controls.Add(browse);
        browse.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="Apps and shortcuts|*.exe;*.lnk;*.url;*.appref-ms;*.bat;*.cmd|All files|*.*",DereferenceLinks=false}) if(d.ShowDialog(this)==DialogResult.OK){PathBox.Text=d.FileName;if(string.IsNullOrWhiteSpace(NameBox.Text))NameBox.Text=System.IO.Path.GetFileNameWithoutExtension(d.FileName);}};
        var save=Theme.Button("Save tile");save.BackColor=Theme.Accent;save.ForeColor=Theme.Background;save.SetBounds(350,172,120,38);Controls.Add(save);
        var cancel=Theme.Button("Cancel");cancel.SetBounds(220,172,115,38);cancel.DialogResult=DialogResult.Cancel;Controls.Add(cancel);CancelButton=cancel;AcceptButton=save;
        save.Click+=(s,e)=>{string p=Environment.ExpandEnvironmentVariables(PathBox.Text.Trim().Trim('"'));if(string.IsNullOrWhiteSpace(NameBox.Text)||(!p.StartsWith("appx:",StringComparison.OrdinalIgnoreCase)&&!File.Exists(p)&&!Directory.Exists(p))){MessageBox.Show(this,"Enter a name and choose an existing app, shortcut, or folder.","Check app");return;} PathBox.Text=p;DialogResult=DialogResult.OK;};
        if(entry!=null){NameBox.Text=entry.Name;PathBox.Text=entry.Path;}
    }
}
class AppBrowser : Form {
    TextBox search=new TextBox();ListView list=new ListView();Label summary=new Label();InstalledApp[] all;
    public InstalledApp SelectedApp;
    public AppBrowser(InstalledApp[] apps){
        all=apps;Text="Choose an app";ClientSize=new Size(680,570);MinimumSize=new Size(520,430);StartPosition=FormStartPosition.CenterParent;BackColor=Theme.Background;ForeColor=Theme.Ink;Font=new Font("Segoe UI",10);
        Controls.Add(new Label{Text="APPS ON THIS PC",Font=new Font("Segoe UI",18,FontStyle.Bold),Location=new Point(20,18),AutoSize=true});
        Controls.Add(new Label{Text="Microsoft Store and desktop apps registered with Windows",ForeColor=Theme.Muted,Location=new Point(22,55),AutoSize=true});
        search.SetBounds(20,88,640,32);search.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(search);search.TextChanged+=(s,e)=>Fill();
        summary.SetBounds(20,130,640,24);summary.ForeColor=Theme.Muted;summary.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(summary);
        list.SetBounds(20,158,640,348);list.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;list.View=View.Details;list.FullRowSelect=true;list.HideSelection=false;list.MultiSelect=false;list.BackColor=Theme.Card;list.ForeColor=Theme.Ink;list.BorderStyle=BorderStyle.FixedSingle;list.Columns.Add("App",430);list.Columns.Add("Suggestion",170);Controls.Add(list);
        var add=Theme.Button("Add to deck");add.SetBounds(530,518,130,38);add.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;add.BackColor=Theme.Accent;add.ForeColor=Theme.Background;Controls.Add(add);
        var cancel=Theme.Button("Cancel");cancel.SetBounds(410,518,110,38);cancel.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;cancel.DialogResult=DialogResult.Cancel;Controls.Add(cancel);CancelButton=cancel;
        add.Click+=(s,e)=>Choose();list.DoubleClick+=(s,e)=>Choose();search.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Down&&list.Items.Count>0){list.Focus();list.Items[0].Selected=true;}};Fill();search.Focus();
    }
    void Fill(){string q=search.Text.Trim();list.BeginUpdate();list.Items.Clear();foreach(var app in all.Where(x=>q.Length==0||x.Name.IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0)){var item=new ListViewItem(app.Name);item.SubItems.Add(app.Score>0?"Recommended":"Installed");item.Tag=app;list.Items.Add(item);}list.EndUpdate();int suggested=all.Count(x=>x.Score>0);summary.Text=(q.Length==0&&suggested>0?suggested+" recommendations · ":"")+list.Items.Count+" apps found";if(list.Items.Count>0)list.Items[0].Selected=true;}
    void Choose(){if(list.SelectedItems.Count==0)return;SelectedApp=(InstalledApp)list.SelectedItems[0].Tag;DialogResult=DialogResult.OK;}
}
public class MainForm : Form {
    DeckStore store; Tile[] tiles=new Tile[15]; TableLayoutPanel grid=new TableLayoutPanel(); Label status=new Label(),count=new Label(); ToolTip tips=new ToolTip(); bool canSave=true;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SHFILEINFO { public IntPtr hIcon;public int iIcon;public uint dwAttributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string szDisplayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string szTypeName; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attr,out SHFILEINFO info,uint size,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx,cy;public SIZE(int width,int height){cx=width;cy=height;} }
    [Flags] enum SIIGBF { ResizeToFit=0, BiggerSizeOk=1, IconOnly=4 }
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellItemImageFactory { void GetImage(SIZE size,SIIGBF flags,out IntPtr bitmap); }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path,IntPtr bindContext,ref Guid riid,[MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    public MainForm(string dataFile) {
        store=new DeckStore(dataFile);Text="LaunchDeck";ClientSize=new Size(1060,810);MinimumSize=new Size(850,730);StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Background;ForeColor=Theme.Ink;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        Icon=SystemIcons.Application;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(28,20,28,16),ColumnCount=1,RowCount=5};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,84));root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,141));root.RowStyles.Add(new RowStyle(SizeType.Absolute,28));Controls.Add(root);
        var header=new Panel{Dock=DockStyle.Fill};root.Controls.Add(header,0,0);
        header.Controls.Add(new Label{Text="LAUNCHDECK",Font=new Font("Segoe UI",21,FontStyle.Bold),Location=new Point(0,0),AutoSize=true});
        header.Controls.Add(new Label{Text="Your apps. One click away.",ForeColor=Theme.Muted,Location=new Point(2,44),AutoSize=true});
        var add=Theme.Button("+  Browse apps");add.Size=new Size(134,40);add.Anchor=AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(add);add.Location=new Point(header.ClientSize.Width-134,10);add.Click+=(s,e)=>BrowseApps();
        var file=Theme.Button("Choose file");file.Size=new Size(112,40);file.Anchor=AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(file);file.Location=new Point(header.ClientSize.Width-258,10);file.Click+=(s,e)=>{int i=EmptySlot();if(i>=0)Edit(i);};
        var update=Theme.Button("Updates");update.Size=new Size(94,40);update.Anchor=AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(update);update.Location=new Point(header.ClientSize.Width-364,10);update.Click+=(s,e)=>Updater.Check(this,SetStatus,true);
        var version=new Label{Text="v"+Updater.Version,ForeColor=Theme.Muted,AutoSize=true};version.Location=new Point(222,11);header.Controls.Add(version);
        var sub=new Panel{Dock=DockStyle.Fill};root.Controls.Add(sub,0,1);sub.Controls.Add(new Label{Text="MY DECK",Font=new Font("Segoe UI",10,FontStyle.Bold),AutoSize=true,Location=new Point(0,7)});count.ForeColor=Theme.Muted;count.AutoSize=true;count.Location=new Point(105,7);sub.Controls.Add(count);
        grid.Dock=DockStyle.Fill;grid.ColumnCount=5;grid.RowCount=3;grid.Margin=Padding.Empty;
        for(int i=0;i<5;i++)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,20));for(int i=0;i<3;i++)grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(grid,0,2);
        for(int i=0;i<15;i++) {int slot=i;var tile=new Tile(i){Dock=DockStyle.Fill,Margin=new Padding(0,0,10,10)};tiles[i]=tile;grid.Controls.Add(tile,i%5,i/5);tile.Click+=(s,e)=>{if(store.Items[slot]==null)Edit(slot);else Launch(slot);};tile.MoveTile=(a,b)=>{store.Swap(a,b);Persist("Tiles rearranged.");};tile.DropFiles=(at,files)=>{int n=store.AddFiles(files,at);Persist(n==0?"No empty tiles available. Remove an app to make space.":n+" app(s) added. Click a tile to launch.");};
            var menu=new ContextMenuStrip();menu.Items.Add("Launch",null,(s,e)=>Launch(slot));menu.Items.Add("Edit / choose app",null,(s,e)=>Edit(slot));menu.Items.Add("Remove from deck",null,(s,e)=>{store.Items[slot]=null;Persist("Tile cleared. Your app is still installed.");});tile.ContextMenuStrip=menu;
        }
        var discovery=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0,9,0,0)};
        for(int i=0;i<3;i++)discovery.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));discovery.RowStyles.Add(new RowStyle(SizeType.Absolute,33));discovery.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(discovery,0,3);
        var discoverTitle=new Label{Text="DISCOVER APPS   /   Official download pages · not paid ads",ForeColor=Theme.Muted,Dock=DockStyle.Fill,AutoSize=true};discovery.Controls.Add(discoverTitle,0,0);discovery.SetColumnSpan(discoverTitle,3);
        Discover(discovery,0,"PowerToys","Windows productivity tools","https://learn.microsoft.com/en-us/windows/powertoys/install");
        Discover(discovery,1,"OBS Studio","Recording & streaming","https://obsproject.com/download");
        Discover(discovery,2,"Find installed apps","Open Windows apps folder","shell:AppsFolder");
        status.Dock=DockStyle.Fill;status.ForeColor=Theme.Muted;status.Font=new Font("Segoe UI",9);status.TextAlign=ContentAlignment.MiddleLeft;root.Controls.Add(status,0,4);
        try{store.Load();}catch(Exception ex){canSave=false;Shown+=(s,e)=>MessageBox.Show(this,"Could not read your saved deck. It will not be overwritten. Restore the .bak file or move the damaged file and restart.\n\n"+store.FileName+"\n\n"+ex.Message,"Saved deck needs attention");}
        RefreshTiles();SetStatus("Drag desktop apps or shortcuts into tiles. Drag tiles to rearrange. Right-click to edit.");Shown+=(s,e)=>Updater.Check(this,SetStatus,false);
    }
    void Discover(TableLayoutPanel panel,int column,string name,string desc,string target) {
        var b=Theme.Button(name+"  ↗\n"+desc);b.Dock=DockStyle.Fill;b.Margin=new Padding(0,0,10,12);b.TextAlign=ContentAlignment.MiddleLeft;b.Padding=new Padding(12,0,0,0);b.FlatAppearance.BorderColor=Theme.Border;b.AccessibleName=name;b.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});SetStatus(target.StartsWith("shell:")?"For Store apps: right-click an app, create a desktop shortcut, then drag it here.":"Download from the publisher, install, then drag its shortcut onto your deck.");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not open");}};panel.Controls.Add(b,column,1);
    }
    void SetStatus(string text){status.Text=text;}
    int EmptySlot(){int i=Array.FindIndex(store.Items,x=>x==null);if(i<0)SetStatus("Your deck is full. Right-click a tile to edit or remove it.");return i;}
    void BrowseApps(){int slot=EmptySlot();if(slot<0)return;SetStatus("Searching apps registered with Windows...");ThreadPool.QueueUserWorkItem(delegate{try{var apps=AppCatalog.Load();BeginInvoke((Action)(()=>{using(var d=new AppBrowser(apps))if(d.ShowDialog(this)==DialogResult.OK){store.Items[slot]=new Entry(d.SelectedApp.Name,"appx:"+d.SelectedApp.Id);Persist(d.SelectedApp.Name+" added to your deck.");}else SetStatus("App search closed.");}));}catch(Exception ex){BeginInvoke((Action)(()=>{SetStatus("App search failed.");MessageBox.Show(this,ex.Message+"\n\nYou can still use Choose file for normal programs and shortcuts.","Could not search apps");}));}});}
    void Edit(int slot){using(var d=new EditDialog(store.Items[slot]))if(d.ShowDialog(this)==DialogResult.OK){store.Items[slot]=new Entry(d.NameBox.Text.Trim(),d.PathBox.Text);Persist("App saved. Click its tile to launch.");}}
    public static ProcessStartInfo LaunchInfo(Entry entry){
        if(entry.Path.StartsWith("appx:",StringComparison.OrdinalIgnoreCase))return new ProcessStartInfo("explorer.exe","shell:AppsFolder\\"+entry.Path.Substring(5)){UseShellExecute=true};
        if(!File.Exists(entry.Path)&&!Directory.Exists(entry.Path))throw new FileNotFoundException("This app or shortcut has moved. Right-click the tile and choose Edit to locate it.",entry.Path);
        return new ProcessStartInfo(entry.Path){UseShellExecute=true,WorkingDirectory=Directory.Exists(entry.Path)?entry.Path:System.IO.Path.GetDirectoryName(entry.Path)};
    }
    void Launch(int slot){if(store.Items[slot]==null)return;try{Process.Start(LaunchInfo(store.Items[slot]));SetStatus("Opened "+store.Items[slot].Name+".");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Couldn't launch app",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
    void Persist(string message){RefreshTiles();try{if(!canSave)throw new IOException("Saved deck needs repair. Changes cannot be saved this session.");store.Save();SetStatus(message);}catch(Exception ex){SetStatus("Changes are not saved.");MessageBox.Show(this,ex.Message,"Could not save deck");}}
    public static Image EntryIcon(Entry entry){
        if(entry.Path.StartsWith("appx:",StringComparison.OrdinalIgnoreCase)){IntPtr bitmap=IntPtr.Zero;try{var id=new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");IShellItemImageFactory factory;SHCreateItemFromParsingName("shell:AppsFolder\\"+entry.Path.Substring(5),IntPtr.Zero,ref id,out factory);factory.GetImage(new SIZE(96,96),SIIGBF.BiggerSizeOk|SIIGBF.IconOnly,out bitmap);using(var image=Image.FromHbitmap(bitmap))return new Bitmap(image);}catch{return null;}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);}}
        SHFILEINFO info;if(SHGetFileInfo(entry.Path,0,out info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100)!=IntPtr.Zero&&info.hIcon!=IntPtr.Zero){try{using(var icon=Icon.FromHandle(info.hIcon))return icon.ToBitmap();}finally{DestroyIcon(info.hIcon);}}return null;
    }
    void RefreshTiles(){for(int i=0;i<15;i++){var t=tiles[i];if(t.AppIcon!=null){t.AppIcon.Dispose();t.AppIcon=null;}t.Entry=store.Items[i];t.AccessibleName=t.Entry==null?"Empty slot "+(i+1)+", add app":t.Entry.Name+", launch app";tips.SetToolTip(t,t.Entry==null?"Drop an app or click to choose one":t.Entry.Path);if(t.Entry!=null)t.AppIcon=EntryIcon(t.Entry)??SystemIcons.Application.ToBitmap();t.Invalidate();}count.Text=store.Items.Count(x=>x!=null)+" / 15 apps";}
    protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
}
static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static int Main(string[] args){
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==3 && args[0]=="--apply-update"){int pid;if(!int.TryParse(args[2],out pid))return 2;return Updater.Apply(args[1],pid);}
        if(args.Length==2 && args[0]=="--launch-probe"){File.WriteAllText(args[1],"launched");return 0;}
        if(args.Length>0 && args[0]=="--self-test")return SelfTest(args.Length>1?args[1]:System.IO.Path.GetTempPath());
        Application.Run(new MainForm(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LaunchDeck","deck.xml")));return 0;
    }
    static int SelfTest(string directory){
        Directory.CreateDirectory(directory);string path=System.IO.Path.Combine(directory,"test-deck.xml");
        try{
            var s=new DeckStore(path);string exe=Application.ExecutablePath;
            if(!Updater.IsNewer("v1.1.2")||Updater.IsNewer("v1.1.1")||Updater.IsNewer("garbage"))throw new Exception("Version comparison failed");
            var appx=MainForm.LaunchInfo(new Entry("Store app","appx:Example.Package!App"));if(appx.FileName!="explorer.exe"||!appx.Arguments.Contains("Example.Package!App"))throw new Exception("Store app launch configuration failed");
            var installed=AppCatalog.Load();if(installed.Length==0)throw new Exception("Installed app discovery failed");
            if(MainForm.EntryIcon(new Entry(installed[0].Name,"appx:"+installed[0].Id))==null)throw new Exception("Registered app icon loading failed");
            if(s.AddFiles(new[]{exe,exe},0)!=2)throw new Exception("Multi-file add failed");
            s.Swap(0,14);s.Items[14].Name="Example & app";s.Save();var loaded=new DeckStore(path);loaded.Load();
            if(loaded.Items[0]!=null||loaded.Items[14].Name!="Example & app"||loaded.Items[1].Path!=exe)throw new Exception("Persistence/reorder failed");
            loaded.Items[1]=null;loaded.Save();var reloaded=new DeckStore(path);reloaded.Load();if(reloaded.Items[1]!=null)throw new Exception("Remove persistence failed");
            var info=MainForm.LaunchInfo(reloaded.Items[14]);if(!info.UseShellExecute||info.FileName!=exe)throw new Exception("Launch configuration failed");
            string probe=System.IO.Path.Combine(directory,"launch probe "+Guid.NewGuid().ToString("N")+".txt");info.Arguments="--launch-probe \""+probe+"\"";using(var process=Process.Start(info)){if(process==null||!process.WaitForExit(10000)||process.ExitCode!=0)throw new Exception("Process launch failed");}if(File.ReadAllText(probe)!="launched")throw new Exception("Process launch probe failed");File.Delete(probe);
            bool missing=false;try{MainForm.LaunchInfo(new Entry("missing",System.IO.Path.Combine(directory,"missing.exe")));}catch(FileNotFoundException){missing=true;}if(!missing)throw new Exception("Missing app handling failed");
            if(reloaded.AddFiles(Enumerable.Repeat(exe,30).ToArray(),14)!=14)throw new Exception("Full-grid handling failed");
            var emptyPath=System.IO.Path.Combine(directory,"empty-deck.xml");
            using(var form=new MainForm(emptyPath)){form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(System.IO.Path.Combine(directory,"launchdeck-preview.png"));}form.Close();}
            File.WriteAllText(System.IO.Path.Combine(directory,"test-results.txt"),"PASS: multi-file import, tile swapping, XML persistence and escaping, removal persistence, launch configuration, missing-target handling, full-grid handling, native form rendering.");return 0;
        }catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(directory,"test-results.txt"),ex.ToString());return 1;}
    }
}
}

