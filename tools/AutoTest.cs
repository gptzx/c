using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
// 동작 테스트 하니스: 실제 MainForm 을 만들어(화면에 띄우지는 않고) 배치·툴팁·타이머·자동 변경(두 방식)·트레이 메뉴 그리기·공개 API 를 확인한다.
// 리플렉션으로 멤버를 찾으므로 일반 빌드와 축소(dist) 빌드에서 같은 검사가 돈다: AutoTest.exe <exe> [names.txt]  (names.txt = tools/Shrink.cs 가 쓴 이름표)
// 빌드: mcs -r:System.Windows.Forms -r:System.Drawing -out:AutoTest.exe tools/AutoTest.cs   (Windows 의 csc 도 동일; Linux 에서는 xvfb-run 아래서 mono 로 실행)
class AutoTest
{
  static int fails;
  static Assembly asm;
  static bool shrunk;
  static readonly Dictionary<string, string> map = new Dictionary<string, string>();      // "T|orig" / "F|origType|name" / "M|origType|name|argc" / "P|origType|name" -> new name
  static readonly Dictionary<string, string> origType = new Dictionary<string, string>();  // new type name -> original full name
  static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
  static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

  static void LoadMap(string path)
  {
    foreach (string line in File.ReadAllLines(path))
    {
      string[] p = line.Split('|');
      if (p[0] == "T") { map["T|" + p[1]] = p[2]; origType[p[2]] = p[1]; }
      else if (p[0] == "M") map["M|" + p[1] + "|" + p[2] + "|" + p[3]] = p[4];
      else map[p[0] + "|" + p[1] + "|" + p[2]] = p[3];
    }
  }
  static string Orig(Type t)
  {
    string full = t.FullName.Replace('+', '/');   // Cecil-style full name ("Outer/Inner")
    string orig;
    return shrunk && origType.TryGetValue(full, out orig) ? orig : full;
  }
  static Type T(string origFull)
  {
    string full = shrunk ? map["T|" + origFull] : origFull;
    return asm.GetType(full.Replace('/', '+'), true);
  }
  static string N(string kind, Type t, string name, int argc)
  {
    if (!shrunk) return name;
    string key = kind + "|" + Orig(t) + "|" + name + (kind == "M" ? "|" + argc : "");
    string n;
    return map.TryGetValue(key, out n) ? n : name;   // virtual/P-Invoke/ctor names are not renamed
  }
  static object Call(object o, string name, params object[] a)
  {
    Type t = o is Type ? (Type)o : o.GetType();
    string n = N("M", t, name, a.Length);
    foreach (MethodInfo m in t.GetMethods(All)) if (m.Name == n && m.GetParameters().Length == a.Length) return m.Invoke(m.IsStatic ? null : (o is Type ? null : o), a);
    throw new Exception("no method " + name + "/" + a.Length + " (" + n + ") on " + t.Name);
  }
  static FieldInfo Fi(Type t, string field) { return t.GetField(N("F", t, field, 0), All); }
  static void Set(object o, string field, object v) { Fi(o.GetType(), field).SetValue(o, v); }
  static T Get<T>(object o, string field) { return (T)Fi(o.GetType(), field).GetValue(o); }
  static bool HasField(Type t, string field) { return Fi(t, field) != null; }
  static void SetProp(object o, string prop, object v) { o.GetType().GetProperty(N("P", o.GetType(), prop, 0), All).SetValue(o, v, null); }
  static object GetProp(object o, string prop) { return o.GetType().GetProperty(N("P", o.GetType(), prop, 0), All).GetValue(o, null); }
  // controls are found through their private fields (the Designer no longer sets Control.Name); removed controls read as null
  static Control C(Form f, string field) { FieldInfo fi = Fi(f.GetType(), field); return fi == null ? null : (Control)fi.GetValue(f); }
  static object New(string origFull) { return Activator.CreateInstance(T(origFull), All, null, new object[0], null); }
  // the control's own Visible flag — Control.Visible reads false for every child while the form is not shown (mono keeps it in is_visible, .NET in GetState(2))
  static bool Vis(Control c)
  {
    FieldInfo fi = typeof(Control).GetField("is_visible", All);
    if (fi != null) return (bool)fi.GetValue(c);
    return (bool)typeof(Control).GetMethod("GetState", All).Invoke(c, new object[] { 2 });
  }

  [STAThread]
  static int Main(string[] args)
  {
    asm = Assembly.LoadFrom(Path.GetFullPath(args[0]));
    shrunk = args.Length > 1;
    if (shrunk) LoadMap(args[1]);
    Console.WriteLine((shrunk ? "shrunk" : "plain") + " build: " + args[0]);

    // ---- public-API checks (formerly Smoke.cs) ----
    Type mac = T("MacChanger.Core.MacAddressUtil");
    Check((string)Call(mac, "Normalize", "02:1a:2b:3c:4d:5e") == "021A2B3C4D5E" && (string)Call(mac, "Normalize", "02-1A-2B-3C-4D-5E") == "021A2B3C4D5E" && (string)Call(mac, "Normalize", "021A.2B3C.4D5E") == "021A2B3C4D5E", "Normalize accepts :, - and . separators");
    Check(Call(mac, "Normalize", "021A2B3C4D5") == null && Call(mac, "Normalize", "zz1A2B3C4D5E") == null && Call(mac, "Normalize", "") == null, "Normalize rejects short / non-hex / empty");
    Check((string)Call(mac, "Format", "021A2B3C4D5E") == "02-1A-2B-3C-4D-5E", "Format: dash-separated upper case");
    Check((bool)Call(mac, "IsUnicast", "021A2B3C4D5E") && !(bool)Call(mac, "IsUnicast", "031A2B3C4D5E") && (bool)Call(mac, "IsUnicast", "0A1A2B3C4D5E"), "IsUnicast: second nibble even");
    Check((bool)Call(mac, "IsAllZero", "000000000000") && !(bool)Call(mac, "IsAllZero", "000000000001"), "IsAllZero");
    int okRandom = 0;
    for (int i = 0; i < 200; i++)
    {
      string w = (string)Call(mac, "GenerateRandom", true), e = (string)Call(mac, "GenerateRandom", false);
      if (w.Length == 12 && "26AE".IndexOf(w[1]) >= 0 && e.Length == 12 && (bool)Call(mac, "IsUnicast", e) && !(bool)Call(mac, "IsAllZero", e)) okRandom++;
    }
    Check(okRandom == 200, "GenerateRandom: wireless uses X2/X6/XA/XE, wired is unicast and never all zero (" + okRandom + "/200)");
    Type dhcp = T("MacChanger.Core.DhcpRelease");
    Check((bool)Call(dhcp, "NameMatchesGuid", "\\DEVICE\\TCPIP_{ABCDEF01-2345-6789-ABCD-EF0123456789}", "{abcdef01-2345-6789-abcd-ef0123456789}") && (bool)Call(dhcp, "NameMatchesGuid", "\\DEVICE\\TCPIP_{ABCDEF01-2345-6789-ABCD-EF0123456789}", "ABCDEF01-2345-6789-ABCD-EF0123456789") && !(bool)Call(dhcp, "NameMatchesGuid", "\\DEVICE\\TCPIP_{ABCDEF01-2345-6789-ABCD-EF0123456789}", "{ABCDEF01-2345-6789-ABCD-EF012345678}") && !(bool)Call(dhcp, "NameMatchesGuid", "x", ""), "NameMatchesGuid: braces optional, case-insensitive, no partial/empty matches");
    Type ipm = T("MacChanger.Core.IpMonitor");
    {
      object[] fa = { "169.254.1.2, 192.168.0.5", "255.255.0.0, 255.255.255.0", null, null };
      Call(ipm, "FilterAssigned", fa);
      Check((string)fa[2] == "192.168.0.5" && (string)fa[3] == "255.255.255.0", "FilterAssigned drops 169.254 addresses with their masks: " + fa[2] + " / " + fa[3]);
      object[] fb = { "169.254.1.2", "255.255.0.0", null, null };
      Call(ipm, "FilterAssigned", fb);
      Check((string)fb[2] == "" && (string)fb[3] == "", "FilterAssigned: only an autoconfig address -> nothing assigned");
    }
    Type tm = T("MacChanger.Core.TrafficMonitor");
    Check((string)Call(tm, "FormatTotal", 24315L) == "23.75 KB (24315 바이트)" && (string)Call(tm, "FormatSpeed", 6193L) == "6.05 KB/s (6193 바이트)", "TMAC-style totals and speeds: " + Call(tm, "FormatTotal", 24315L) + " | " + Call(tm, "FormatSpeed", 6193L));
    Type nai = T("MacChanger.Core.NetworkAdapterInfo");
    Check((string)Call(nai, "NormalizeGuid", "{ABCDEF01-2345-6789-ABCD-EF0123456789}") == (string)Call(nai, "NormalizeGuid", "abcdef01-2345-6789-abcd-ef0123456789"), "NormalizeGuid ignores braces and case");
    Check((bool)Call(nai, "LooksWireless", "Intel(R) Wi-Fi 6 AX200") && (bool)Call(nai, "LooksWireless", "Realtek Wireless LAN") && !(bool)Call(nai, "LooksWireless", "Realtek PCIe GbE Family Controller"), "LooksWireless heuristics");

    Form f = (Form)New("MacChanger.MainForm");
    IntPtr handle = f.Handle;   // handle needed for BeginInvoke-based tests
    // ---- layout ----
    Check(f.ClientSize == new Size(560, 714), "client size 560x714 (one row more than before): " + f.ClientSize);
    Control graph = C(f, "trafficGraph"), logBox = C(f, "txtIpLog");
    Check(graph.Size == new Size(262, 167) && logBox.Size == new Size(262, 167) && graph.Top == logBox.Top && graph.Left > logBox.Right && logBox.Bottom + 12 == f.ClientSize.Height, "log box and graph 262x167 side by side with a 12 px bottom margin: " + logBox.Bounds + " / " + graph.Bounds);
    int overlaps = 0, outside = 0;
    List<Control> all = new List<Control>();
    foreach (Control c in f.Controls) if (Vis(c) && c.Width > 0 && c.Height > 0) all.Add(c);
    for (int i = 0; i < all.Count; i++)
    {
      Rectangle a = all[i].Bounds;
      if (a.Right > f.ClientSize.Width || a.Bottom > f.ClientSize.Height || a.Left < 0 || a.Top < 0) { outside++; Console.WriteLine("  outside: " + all[i].GetType().Name + " " + a); }
      for (int j = i + 1; j < all.Count; j++) if (a.IntersectsWith(all[j].Bounds)) { overlaps++; Console.WriteLine("  overlap: " + a + " / " + all[j].Bounds); }
    }
    Check(overlaps == 0 && outside == 0 && all.Count == 53, "no overlapping controls, all 53 visible controls (56 minus the hidden interval row) inside " + f.ClientSize + " (" + all.Count + " checked)");
    Check(C(f, "nudIpInterval") == null && C(f, "btnReleaseIp") != null && C(f, "chkReleaseIp") != null && C(f, "modeSwitch") != null && C(f, "nudInterval") != null, "IP 반환 and interval-mode controls present");
    Check(C(f, "btnReleaseIp").Text == "즉시 반환" && C(f, "chkReleaseIp").Text == "DHCP 서버에 IP 반환" && C(f, "btnClearLog").Text == "로그 비우기" && C(f, "chkLogBox").Text == "로그 상자 사용" && C(f, "chkAuto").Text == "사용" && C(f, "lblModeIp").Text == "IP 할당 시" && C(f, "lblModeTimer").Text == "일정 시간마다" && C(f, "lblDelayPrefix").Text == "IP 할당 후", "button/checkbox/label texts");
    // tab order: row by row, the way the eye reads the window
    List<string> order = new List<string>();
    for (Control cur = f.GetNextControl(null, true); cur != null; cur = f.GetNextControl(cur, true)) if (cur.TabStop && Vis(cur)) order.Add(cur == C(f, "cboAdapters") ? "cbo" : cur.Text);
    string tabs = string.Join(",", order.ToArray());
    Check(tabs == "cbo,새로 고침,,랜덤 MAC 주소,DNS 초기화,변경 적용,IP 갱신,원상복구,로그 비우기,사용,,시작,10,30,지정 IP/대역 할당 시 정지,,DHCP 서버에 IP 반환,즉시 반환,시간,서브넷 마스크,기본 게이트웨이,MAC,로그 상자 사용,할당 IP 로그 저장,백그라운드 실행", "tab order follows the rows (switch after 사용; the hidden interval field is skipped): " + tabs);
    Check(C(f, "lblModeIp").Cursor == Cursors.Hand && C(f, "lblModeTimer").Cursor == Cursors.Hand && C(f, "modeSwitch").AccessibleName == "IP 할당 시", "side labels show a hand cursor; the switch has an accessible name");
    // ---- tooltips ----
    ToolTip tip = Get<ToolTip>(f, "toolTip");
    int tips = 0, longLines = 0;
    foreach (Control c in f.Controls)
    {
      string tt = tip.GetToolTip(c);
      if (string.IsNullOrEmpty(tt)) continue;
      tips++;
      foreach (string line in tt.Split('\n')) if (line.Length > 70) { longLines++; Console.WriteLine("  long tooltip line: " + line); }
    }
    Check(tips >= 27 && longLines == 0, "tooltips on " + tips + " controls, every line <= 70 chars");
    Check(!tip.GetToolTip(C(f, "btnBackground")).Contains("두 번") && !tip.GetToolTip(C(f, "chkReleaseIp")).Contains("ipconfig") && tip.GetToolTip(C(f, "modeSwitch")).Length > 0 && tip.GetToolTip(C(f, "nudInterval")).Length > 0, "tooltips: single click wording for the tray, no ipconfig mention, switch and interval have tips");
    // ---- log box ----
    CheckBox logChk = (CheckBox)C(f, "chkLogBox"); Button clear = (Button)C(f, "btnClearLog");
    Check(!logChk.Checked && !logBox.Enabled && clear.Enabled, "default: log box present but disabled, 로그 비우기 usable");
    logChk.Checked = true; Check(logBox.Enabled && clear.Enabled, "ticking enables the box");
    logChk.Checked = false; Check(!logBox.Enabled && clear.Enabled, "unticking disables the box, 로그 비우기 still usable");
    // ---- tray icon + custom popup menu ----
    NotifyIcon tray = Get<NotifyIcon>(f, "notifyIcon");
    Check(!tray.Visible && tray.ContextMenuStrip == null && tray.Icon != null, "tray icon hidden at start, no ContextMenuStrip (custom TrayMenu popup instead), icon set");
    {
      Type tmt = T("MacChanger.TrayMenu");
      Check(tmt.IsSubclassOf(typeof(Form)), "TrayMenu is a Form");
      Rectangle work = new Rectangle(0, 0, 1920, 1040);
      Size sz = new Size(100, 52);
      Point p1 = (Point)Call(tmt, "PlacePanel", new Point(1850, 1035), sz, work);
      Point p2 = (Point)Call(tmt, "PlacePanel", new Point(500, 500), sz, work);
      Point p3 = (Point)Call(tmt, "PlacePanel", new Point(-50, 2000), sz, work);
      Check(p1 == new Point(1750, 983) && p2 == new Point(500, 500) && p3 == new Point(0, 988), "PlacePanel: flips up-left near the bottom-right, stays at the cursor elsewhere, clamps into the work area: " + p1 + p2 + p3);
      Form menu = (Form)Activator.CreateInstance(tmt, All, null, new object[] { f }, null);
      Call(menu, "AddItem", "열기", new Action(delegate { }));
      Call(menu, "AddItem", "종료", new Action(delegate { }));
      Size panelSize = (Size)Call(menu, "MeasurePanel");
      float scale = Get<float>(menu, "scale");
      int margin = (int)Math.Round(16 * scale), item = (int)Math.Round(22 * scale), pad = (int)Math.Round(4 * scale);
      Check(scale >= 1 && panelSize.Width >= (int)Math.Round(100 * scale) && panelSize.Height == 2 * item + 2 * pad, "panel: min width 100, two 22 px items + 4 px padding (scale " + scale + "): " + panelSize);
      Rectangle panel = new Rectangle(margin, margin, panelSize.Width, panelSize.Height);
      Set(menu, "panel", panel);
      menu.Bounds = new Rectangle(0, 0, panelSize.Width + 2 * margin, panelSize.Height + 2 * margin);
      int mx = panel.X + panel.Width / 2, my = panel.Y + panel.Height / 2;
      using (Bitmap bmp = (Bitmap)Call(menu, "RenderBitmap"))
      {
        Color corner = bmp.GetPixel(0, 0), inside = bmp.GetPixel(mx, my), left = bmp.GetPixel(panel.X, my), top = bmp.GetPixel(mx, panel.Y);
        Color below1 = bmp.GetPixel(mx, panel.Bottom + 1), below8 = bmp.GetPixel(mx, panel.Bottom + 8), above2 = bmp.GetPixel(mx, panel.Y - 2), side3 = bmp.GetPixel(panel.X - 3, my);
        Color cornerPx = bmp.GetPixel(panel.X, panel.Y);
        Check(bmp.Size == menu.Size && corner.A == 0 && inside.ToArgb() == Color.FromArgb(249, 249, 249).ToArgb(), "bitmap: transparent far corner, opaque 249 panel interior: " + corner + " / " + inside);
        Check(left.A == 255 && left.R == 229 && left.G == 229 && left.B == 229 && top.R == 229 && top.A == 255, "1 px border 229 on the panel edges: " + left + " / " + top);
        Check(below1.A >= 15 && below1.A <= 30 && below8.A > 0 && below8.A < below1.A && side3.A >= 4 && side3.A <= 16 && above2.A < 4 && below1.R == 0 && below1.G == 0 && below1.B == 0, "aura: black shadow about 9% right below, about 3% beside, almost none above, fading outwards: below " + below1.A + " > " + below8.A + ", side " + side3.A + ", above " + above2.A);
        Check(cornerPx.A < 255 || (cornerPx.R < 240 && cornerPx.R != 229), "rounded corner: the panel's corner pixel is not panel colour: " + cornerPx);
        int dark = 0;
        for (int y = panel.Y + pad; y < panel.Y + pad + item; y++) for (int x = panel.X + 14; x < panel.X + 60; x++) if (bmp.GetPixel(x, y).R < 128) dark++;
        int gutter = 0;
        for (int y = panel.Y + 5; y < panel.Bottom - 5; y++) for (int x = panel.X + 5; x < panel.X + 12; x++) if (bmp.GetPixel(x, y).ToArgb() != Color.FromArgb(249, 249, 249).ToArgb()) gutter++;
        Check(dark > 10 && gutter == 0, "item text rendered in the first row (dark px " + dark + "), no grey gutter column on the left (" + gutter + ")");
      }
      Set(menu, "hover", 1);
      using (Bitmap bmp = (Bitmap)Call(menu, "RenderBitmap"))
      {
        Color h = bmp.GetPixel(panel.Right - 12, panel.Y + pad + item + item / 2), n = bmp.GetPixel(panel.Right - 12, panel.Y + pad + item / 2);
        Check(h.ToArgb() == Color.FromArgb(234, 234, 234).ToArgb() && n.ToArgb() == Color.FromArgb(249, 249, 249).ToArgb(), "hover highlight 234 on the hovered row only: " + h + " / " + n);
      }
      menu.Dispose();
    }
    // ---- mode switch control ----
    Control sw = C(f, "modeSwitch");
    {
      int flips = 0;
      EventInfo ev = sw.GetType().GetEvent(N("E", sw.GetType(), "CheckedChanged", 0), All);
      ev.AddEventHandler(sw, new EventHandler(delegate { flips++; }));
      Check(sw.Size == new Size(44, 20) && sw.TabStop && !(bool)GetProp(sw, "Checked"), "switch: 44x20, tab stop, left (IP 할당 시) by default");
      SetProp(sw, "Checked", true); SetProp(sw, "Checked", true);
      Check((bool)GetProp(sw, "Checked") && flips == 1, "setting Checked raises CheckedChanged once per real change");
      Call(sw, "OnClick", EventArgs.Empty);
      Check(!(bool)GetProp(sw, "Checked") && flips == 2, "a click flips it back");
      Call(sw, "OnClick", new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0)); Call(sw, "OnClick", new MouseEventArgs(MouseButtons.Middle, 1, 5, 5, 0));
      Check(!(bool)GetProp(sw, "Checked") && flips == 2, "right/middle mouse clicks do not flip the switch");
      Call(sw, "OnClick", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0));
      Check((bool)GetProp(sw, "Checked") && flips == 3, "a left mouse click does");
      SetProp(sw, "Checked", false); flips = 2;
      Call(sw, "OnKeyDown", new KeyEventArgs(Keys.Right)); bool right = (bool)GetProp(sw, "Checked");
      Call(sw, "OnKeyDown", new KeyEventArgs(Keys.Left)); bool left = !(bool)GetProp(sw, "Checked");
      Call(sw, "OnKeyDown", new KeyEventArgs(Keys.Space)); Call(sw, "OnKeyDown", new KeyEventArgs(Keys.Space)); Call(sw, "OnKeyDown", new KeyEventArgs(Keys.Space));
      bool heldNoToggle = !(bool)GetProp(sw, "Checked");
      Call(sw, "OnKeyUp", new KeyEventArgs(Keys.Space)); bool space = (bool)GetProp(sw, "Checked");
      Call(sw, "OnKeyUp", new KeyEventArgs(Keys.Enter)); bool strayUp = (bool)GetProp(sw, "Checked");
      Call(sw, "OnKeyDown", new KeyEventArgs(Keys.Enter)); Call(sw, "OnKeyUp", new KeyEventArgs(Keys.Enter)); bool enter = !(bool)GetProp(sw, "Checked");
      Check(right && left && heldNoToggle && space && strayUp && enter && (bool)Call(sw, "IsInputKey", Keys.Left) && (bool)Call(sw, "IsInputKey", Keys.Right) && !(bool)Call(sw, "IsInputKey", Keys.Tab), "keyboard: →/← select; Space/Enter toggle once on key-up (held key does not repeat, a stray key-up is ignored); arrows are input keys, Tab is not");
      Check(!(bool)Call(sw, "GetStyle", ControlStyles.StandardDoubleClick), "a quick second click is a second toggle (StandardDoubleClick off, like CheckBox)");
      Check(sw.AccessibleName == "IP 할당 시", "accessible name = the selected side: " + sw.AccessibleName);
      SetProp(sw, "Checked", true); bool accRight = sw.AccessibleName == "일정 시간마다"; SetProp(sw, "Checked", false); flips = 2;
      Check(accRight && sw.AccessibleName == "IP 할당 시", "accessible name follows the switch");
      using (Bitmap bmp = new Bitmap(44, 20))
      {
        using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.Magenta); Call(sw, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 44, 20))); }
        Color knobL = bmp.GetPixel(10, 10), trackR = bmp.GetPixel(33, 10), far = bmp.GetPixel(0, 0), mid = bmp.GetPixel(22, 10), bottom = bmp.GetPixel(22, 19), lastRow = bmp.GetPixel(22, 18);
        Check(knobL.R > 240 && knobL.G > 240 && knobL.B > 240 && trackR.ToArgb() == SystemColors.Highlight.ToArgb() && mid.ToArgb() == SystemColors.Highlight.ToArgb() && far.ToArgb() == Color.Magenta.ToArgb() && bottom.ToArgb() == Color.Magenta.ToArgb() && lastRow.ToArgb() == SystemColors.Highlight.ToArgb(), "paint (left): white knob at the left, highlight track at the right, rounded corner transparent, pill ends one row above the bottom like its caps: " + knobL + " / " + trackR + " / " + far + " / " + bottom);
        SetProp(sw, "Checked", true);
        using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.Magenta); Call(sw, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 44, 20))); }
        Color knobR = bmp.GetPixel(33, 10), trackL = bmp.GetPixel(10, 10);
        Check(knobR.R > 240 && knobR.G > 240 && trackL.ToArgb() == SystemColors.Highlight.ToArgb(), "paint (right): knob moved to the right");
        sw.Enabled = false;
        using (Graphics g = Graphics.FromImage(bmp)) { g.Clear(Color.Magenta); Call(sw, "OnPaint", new PaintEventArgs(g, new Rectangle(0, 0, 44, 20))); }
        Check(bmp.GetPixel(10, 10).ToArgb() == SystemColors.ControlDark.ToArgb(), "paint (disabled): grey track");
        sw.Enabled = true;
        SetProp(sw, "Checked", false);
      }
    }
    // ---- timers and IP monitoring ----
    System.Windows.Forms.Timer t = Get<System.Windows.Forms.Timer>(f, "secondTimer"), ip = Get<System.Windows.Forms.Timer>(f, "ipTimer"), g2 = Get<System.Windows.Forms.Timer>(f, "graphTimer");
    Check(g2.Interval == 1000 && !g2.Enabled, "graph has its own 1000 ms timer");
    g2.Stop();
    if (!shrunk) Check(t.Interval == 1000 && (int)Fi(f.GetType(), "IpSafetyPollMs").GetValue(null) == 3000 && (int)Fi(f.GetType(), "IpSettleMs").GetValue(null) == 1000 && (int)Fi(f.GetType(), "IpFallbackPollMs").GetValue(null) == 1000, "graph/countdown timer 1000 ms; IP timer: safety 3000 ms, settle 1000 ms, fallback 1000 ms");
    else Check(t.Interval == 1000 && !HasField(f.GetType(), "IpSafetyPollMs"), "countdown timer 1000 ms; constants are inlined (no const fields left in the shrunk build)");
    Call(f, "StartIpMonitoring");
    object notifier = Get<object>(f, "ipNotifier");
    Check(ip.Enabled && (notifier == null ? ip.Interval == 1000 : ip.Interval == 3000), "StartIpMonitoring: IP timer running at " + ip.Interval + " ms (notifier " + (notifier == null ? "unavailable here" : "registered") + ")");
    t.Stop(); ip.Stop();
    Set(f, "refreshQueued", 0);
    Call(f, "ipNotifier_Changed", null, EventArgs.Empty);
    Check(Get<int>(f, "refreshQueued") == 1, "notification queues one UI refresh");
    Call(f, "ipNotifier_Changed", null, EventArgs.Empty);
    Check(Get<int>(f, "refreshQueued") == 1, "a second notification before the refresh ran is coalesced");
    Application.DoEvents();
    Check(Get<int>(f, "refreshQueued") == 0 && ip.Enabled && ip.Interval == 1000 && !t.Enabled, "queued refresh ran on the UI thread, cleared the flag, armed the 1 s settle on the IP timer and left the graph timer alone");
    ip.Stop();
    Call(f, "ipTimer_Tick", null, EventArgs.Empty);
    Check(ip.Interval == (notifier == null ? 1000 : 3000) && !t.Enabled, "after the settle read the IP timer returns to its safety/fallback period; the graph timer is still untouched");
    ip.Stop();
    // ---- auto change: gating, countdown text, formats ----
    CheckBox chk = (CheckBox)C(f, "chkAuto"); NumericUpDown nud = (NumericUpDown)C(f, "nudAutoDelay"), waitNud = (NumericUpDown)C(f, "nudWaitTimeout"), nudInt = (NumericUpDown)C(f, "nudInterval");
    Button btn = (Button)C(f, "btnAutoToggle"); CheckBox stopChk = (CheckBox)C(f, "chkAutoStopIp"); TextBox stopBox = (TextBox)C(f, "txtAutoStopIp");
    Label lblModeIp = (Label)C(f, "lblModeIp"), lblModeTimer = (Label)C(f, "lblModeTimer"), lblDelayPrefix = (Label)C(f, "lblDelayPrefix"), lblIntervalPrefix = (Label)C(f, "lblIntervalPrefix"), lblWait = (Label)C(f, "lblWait"), status = (Label)C(f, "lblStatus");
    Check(!chk.Checked && !btn.Enabled && !nud.Enabled && !waitNud.Enabled && !nudInt.Enabled && !stopBox.Enabled, "default: 사용 unchecked -> 시작, delay, wait seconds, interval and stop IP box locked");
    Check(nudInt.Minimum == 10 && nudInt.Maximum == 604800 && nudInt.Value == 30 && nud.Minimum == 0 && nud.Maximum == 604800 && nud.Value == 10 && waitNud.Minimum == 10 && waitNud.Maximum == 60 && waitNud.Value == 30, "ranges: interval 10..604800 (default 30), delay 0..604800 (10), wait 10..60 (30)");
    Check(Vis(lblDelayPrefix) && Vis(nud) && !Vis(lblIntervalPrefix) && !Vis(nudInt) && lblWait.ForeColor == SystemColors.ControlText && lblModeIp.ForeColor == SystemColors.ControlText && lblModeTimer.ForeColor == SystemColors.GrayText && nud.Location == nudInt.Location && lblDelayPrefix.Location == lblIntervalPrefix.Location, "default mode IP 할당 시: the IP setting row is shown, the interval row hidden in the same place, left label black / right label grey");
    chk.Checked = true;
    Check(btn.Enabled && nud.Enabled && waitNud.Enabled && !nudInt.Enabled && btn.Text == "시작" && status.Text.Contains("'시작'을 누르면"), "ticking 사용 (IP mode) enables 시작 + delay + wait seconds, interval stays locked, status explains that 시작 is still needed: " + status.Text);
    Call(f, "lblMode_Click", lblModeTimer, EventArgs.Empty);
    Check((bool)GetProp(sw, "Checked") && !nud.Enabled && !Vis(nud) && !waitNud.Enabled && nudInt.Enabled && Vis(nudInt) && Vis(lblIntervalPrefix) && !Vis(lblDelayPrefix) && lblWait.ForeColor == SystemColors.GrayText && lblModeTimer.ForeColor == SystemColors.ControlText && lblModeIp.ForeColor == SystemColors.GrayText && status.Text.Contains("일정 시간마다"), "clicking 일정 시간마다: interval row shown and enabled, IP row hidden, wait row grey, status names the mode: " + status.Text);
    Call(f, "lblMode_Click", lblModeIp, EventArgs.Empty);
    Check(!(bool)GetProp(sw, "Checked") && nud.Enabled && Vis(nud) && waitNud.Enabled && !Vis(nudInt) && lblWait.ForeColor == SystemColors.ControlText && status.Text.Contains("IP 할당 시"), "clicking IP 할당 시 switches back");
    stopChk.Checked = true; stopBox.Text = "10.0";
    Check(stopBox.Enabled, "ticking 자동 정지 opens the IP box");
    stopChk.Checked = false;
    Check(!stopBox.Enabled && stopBox.Text == "", "unticking 자동 정지 locks and clears the IP box");
    Check((string)Call(f, "FormatRemaining", 59) == "59초" && (string)Call(f, "FormatRemaining", 3600) == "1:00:00" && (string)Call(f, "FormatRemaining", 90061) == "1일 1:01:01", "FormatRemaining: seconds, H:MM:SS, D일 H:MM:SS: " + Call(f, "FormatRemaining", 90061));
    Set(f, "operationReason", "IP 할당 대기 30초 초과");
    Check((string)Call(f, "ReasonPrefix") == "IP 할당 대기 30초 초과 — ", "ReasonPrefix: reason + em dash");
    Set(f, "operationReason", null);
    Check((string)Call(f, "ReasonPrefix") == "", "ReasonPrefix: empty when there is no reason");
    // ---- stop-IP parsing ----
    string[,] match = { {"123.45.6.7","123","123.45.6.7"}, {"124.0.0.1","123","null"}, {"1123.1.1.1","123","null"},
                        {"10.0.0.1, 123.123.5.6","123.123","123.123.5.6"}, {"123.124.0.1","123.123","null"},
                        {"123.123.123.9","123.123.123","123.123.123.9"}, {"123.123.122.9","123.123.123","null"},
                        {"123.123.123.123","123.123.123.123","123.123.123.123"}, {"123.123.123.12","123.123.123.123","null"},
                        {"12.0.0.1","123","null"} };
    int mbad = 0;
    for (int i = 0; i < match.GetLength(0); i++)
    {
      object r = Call(f, "FirstIpInPrefix", match[i, 0], match[i, 1]);
      string got = r == null ? "null" : (string)r;
      if (got != match[i, 2]) { mbad++; Console.WriteLine("  FirstIpInPrefix('" + match[i, 0] + "', '" + match[i, 1] + "') = " + got + ", want " + match[i, 2]); }
    }
    Check(mbad == 0, "FirstIpInPrefix: /8, /16, /24 and exact matching per octet");
    Check((string)Call(f, "StopTargetLabel", "123.123") == "지정한 대역 123.123.* 의" && (string)Call(f, "StopTargetLabel", "1.2.3.4") == "지정한", "StopTargetLabel wording (/16, exact)");
    stopChk.Checked = true; stopBox.Text = "192.168.001.010";
    Check((string)Call(f, "ParseStopIp") == "192.168.1.10", "ParseStopIp reads octets as decimal (leading zeros dropped)");
    stopBox.Text = "300.1"; Check(Call(f, "ParseStopIp") == null, "ParseStopIp rejects an octet above 255");
    stopBox.Text = "123.123";
    // ---- auto change: locks while running, deferred decision on the requested IP read ----
    Call(f, "StartAuto");
    string[] locked = { "btnApply", "btnRenewIp", "btnRestore", "btnReleaseIp", "btnRandom", "btnRefresh", "cboAdapters", "txtNewMac", "nudAutoDelay", "nudWaitTimeout", "nudInterval", "modeSwitch", "txtAutoStopIp", "chkAutoStopIp", "chkResetDns", "chkReleaseIp", "chkAuto" };
    string[] free = { "btnAutoToggle", "btnClearLog", "btnBackground" };
    int bad = 0;
    foreach (string n in locked) if (C(f, n).Enabled) { bad++; Console.WriteLine("  still enabled while running: " + n); }
    foreach (string n in free) if (!C(f, n).Enabled) { bad++; Console.WriteLine("  wrongly disabled while running: " + n); }
    Check(bad == 0 && Get<bool>(f, "autoRunning") && btn.Text == "정지 (IP 대기)" && lblModeIp.Cursor == Cursors.Default && lblModeTimer.Cursor == Cursors.Default, "StartAuto locks everything except 정지 / 로그 비우기 / 백그라운드 실행 (switch included, side labels lose the hand cursor) and shows 정지 (IP 대기)");
    Call(f, "StopAuto");
    Check(!Get<bool>(f, "autoRunning") && btn.Text == "시작" && C(f, "btnApply").Enabled && nud.Enabled && stopBox.Enabled && sw.Enabled && lblModeIp.Cursor == Cursors.Hand, "StopAuto unlocks everything (hand cursor back on the side labels)");
    // StartAuto only requests an IP read; the decision (stop target / schedule) runs in ApplyIpInfo on that read.
    // A fake adapter stands in for hardware (selection handler suppressed), and readings are fed to ApplyIpInfo directly.
    object fake = New("MacChanger.Core.NetworkAdapterInfo");
    SetProp(fake, "InterfaceGuid", "{11111111-2222-3333-4444-555555555555}"); SetProp(fake, "Description", "test"); SetProp(fake, "ConnectionName", "test"); SetProp(fake, "CurrentMac", "001122334455");
    string fakeGuid = (string)GetProp(fake, "InterfaceGuid");
    ComboBox cbo = (ComboBox)C(f, "cboAdapters");
    Set(f, "suppressSelectionChanged", true); cbo.Items.Add(fake); cbo.SelectedItem = fake; Set(f, "suppressSelectionChanged", false);
    Check(cbo.GetItemText(fake) == fake.ToString() && fake.ToString() == "[유선] test - 00-11-22-33-44-55", "drop-down item text comes from NetworkAdapterInfo.ToString (no formatting path differences): " + cbo.GetItemText(fake));
    object reading = New("MacChanger.Core.IpMonitor/IpInfo");
    Set(reading, "Up", true); Set(reading, "Masks", "255.255.255.0"); Set(reading, "Gateways", ""); Set(reading, "Dns", "");
    nud.Value = 0;
    Call(f, "StartAuto");
    Check(Get<bool>(f, "autoRunning") && !Get<bool>(f, "autoPending") && btn.Text == "정지 (IP 대기)", "StartAuto requests a read and waits for it before deciding: " + btn.Text);
    Set(reading, "Addresses", "123.123.7.7");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(!Get<bool>(f, "autoRunning") && !Get<bool>(f, "autoPending") && status.Text.Contains("123.123.*") && status.Text.Contains("123.123.7.7"), "prefix already matched on the first read (delay 0): stops without changing, band + IP in the status: " + status.Text);
    nud.Value = 5;
    Call(f, "StartAuto");
    Set(reading, "Addresses", "123.124.7.7");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<bool>(f, "autoRunning") && Get<bool>(f, "autoPending") && btn.Text == "정지 (5초)" && !stopBox.Enabled && !waitNud.Enabled && !nud.Enabled && !stopChk.Enabled, "outside the band: the first read schedules the change (" + btn.Text + "), and all auto options lock while running");
    Check(t.Enabled && !g2.Enabled, "ScheduleAuto re-phases only the countdown timer; the graph timer is never touched");
    t.Stop();
    int samples = Get<int>(graph, "count");
    Call(f, "secondTimer_Tick", null, EventArgs.Empty);
    Check(Get<int>(graph, "count") == samples, "the countdown tick no longer samples the graph");
    Call(f, "graphTimer_Tick", null, EventArgs.Empty);
    Check(Get<int>(graph, "count") == samples + 1, "the graph tick adds exactly one sample");
    int due = Get<int>(f, "autoDueTick");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<int>(f, "autoDueTick") == due && Get<string>(f, "lastLoggedIp") == "123.124.7.7", "the same IP read again while the countdown is pending neither reschedules nor logs again");
    Set(f, "autoPending", false);
    Set(reading, "Addresses", "9.9.9.9");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch") - 1);
    Check(!Get<bool>(f, "autoPending") && Get<string>(f, "lastLoggedIp") == "123.124.7.7", "a reading that straddled an operation (older epoch) is dropped");
    Set(reading, "Addresses", "123.124.7.7");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<bool>(f, "autoPending") && Get<string>(f, "lastLoggedIp") == "123.124.7.7", "an unchanged IP with no countdown pending (auto just started, or the IP came back) schedules without a duplicate log line");
    // countdown text follows the real clock, one digit per second, never skipping
    int now = Environment.TickCount; List<string> seen = new List<string>();
    for (int s = 5; s >= 2; s--) { Set(f, "autoDueTick", unchecked(now + s * 1000 - 1)); Call(f, "RunAutoIfDue"); seen.Add(btn.Text); }
    Check(seen[0] == "정지 (5초)" && seen[1] == "정지 (4초)" && seen[2] == "정지 (3초)" && seen[3] == "정지 (2초)", "countdown: ceil(remaining) per second: " + string.Join(" ", seen.ToArray()));
    Set(f, "autoDueTick", unchecked(now + 5000));
    // no IP on the read: countdown cancelled, wait clock starts and the timer is re-phased
    Set(reading, "Addresses", ""); Set(reading, "Up", true);
    t.Stop();
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(!Get<bool>(f, "autoPending") && Get<bool>(f, "waiting") && btn.Text == "정지 (IP 대기 0초)" && t.Enabled && !g2.Enabled, "link up without IP: countdown cancelled, wait clock starts at 0초 and re-phases the 1 s timer to the wait start (so 0초→1초 also takes exactly 1 s); graph timer untouched");
    t.Stop();
    Set(f, "waitingSinceTick", unchecked(Environment.TickCount - 1500));
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(btn.Text == "정지 (IP 대기 0초)", "an IP read between ticks does not advance the digit (" + btn.Text + ")");
    Call(f, "secondTimer_Tick", null, EventArgs.Empty);
    Check(btn.Text == "정지 (IP 대기 1초)" && !t.Enabled, "the tick advances it to 1초 (" + btn.Text + ")");
    Call(f, "StopAuto");
    Check(stopBox.Enabled && waitNud.Enabled && nud.Enabled && stopChk.Enabled, "stopping unlocks the options again");
    // operation completion while auto-change runs: button back to "정지 (IP 대기)", MAC shown from the worker's result (no re-read on the UI thread)
    Set(f, "autoRunning", true); Set(f, "autoPending", false); Set(f, "waiting", false); btn.Text = "정지";
    object res = New("MacChanger.Core.MacChangeResult");
    SetProp(res, "Success", true); SetProp(res, "AdapterRestarted", true); SetProp(res, "CurrentMac", "02AABBCCDDEE"); SetProp(res, "PermanentMac", "001122334455"); SetProp(res, "Message", "ok");
    ((TextBox)C(f, "txtPermanentMac")).Text = "(조회 실패)";
    Call(f, "worker_RunWorkerCompleted", null, new System.ComponentModel.RunWorkerCompletedEventArgs(res, null, false));
    Check(btn.Text == "정지 (IP 대기)" && Get<bool>(f, "autoRunning") && ((TextBox)C(f, "txtCurrentMac")).Text == "02-AA-BB-CC-DD-EE" && ((TextBox)C(f, "txtPermanentMac")).Text == "00-11-22-33-44-55" && (string)GetProp(fake, "CurrentMac") == "02AABBCCDDEE" && Get<string>(f, "lastLoggedIp") == null && !g2.Enabled, "completion: label 정지 (IP 대기), current and permanent MAC from the result, log baseline reset, graph timer untouched: " + btn.Text);
    ((TextBox)C(f, "txtCurrentMac")).Text = "(조회 실패)";
    Call(f, "ShowCurrentMac", fake, "02AABBCCDDEE");
    Check(((TextBox)C(f, "txtCurrentMac")).Text == "02-AA-BB-CC-DD-EE", "ShowCurrentMac refreshes the box even when the adapter already holds that MAC");
    Call(f, "StopAuto");
    Set(f, "waiting", false); Set(f, "lastLoggedIp", null);
    // ---- interval mode ("일정 시간마다") ----
    stopChk.Checked = false;
    SetProp(sw, "Checked", true);
    nudInt.Value = 60;
    t.Stop();
    Call(f, "StartAuto");
    int dueInt = Get<int>(f, "autoDueTick");
    int remaining = unchecked(dueInt - Environment.TickCount);
    Check(Get<bool>(f, "autoRunning") && Get<bool>(f, "autoPending") && btn.Text == "정지 (60초)" && remaining > 59000 && remaining <= 60000 && t.Enabled && !g2.Enabled && !sw.Enabled && !nudInt.Enabled && !chk.Enabled && status.Text.Contains("IP와 관계없이 60초마다"), "interval start: first change due in 60 s, countdown shown at once, countdown timer re-phased, graph timer untouched, switch/interval/사용 locked: " + btn.Text + " / " + status.Text);
    t.Stop();
    Set(reading, "Addresses", ""); Set(reading, "Up", true);
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<bool>(f, "autoPending") && Get<int>(f, "autoDueTick") == dueInt && btn.Text == "정지 (60초)" && !Get<bool>(f, "waiting") && !t.Enabled, "interval mode: link up without IP neither cancels the schedule nor starts the IP 대기 clock");
    Set(reading, "Addresses", "123.124.7.7");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<bool>(f, "autoPending") && Get<int>(f, "autoDueTick") == dueInt && btn.Text == "정지 (60초)" && Get<string>(f, "lastLoggedIp") == "123.124.7.7" && !t.Enabled, "interval mode: a new IP is logged but does not reschedule");
    Set(reading, "Addresses", "");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<bool>(f, "autoPending") && Get<int>(f, "autoDueTick") == dueInt && Get<string>(f, "lastLoggedIp") == null && btn.Text == "정지 (60초)", "interval mode: losing the IP again still keeps the schedule");
    Set(reading, "Addresses", "169.254.3.4");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(Get<bool>(f, "autoPending") && Get<int>(f, "autoDueTick") == dueInt && btn.Text == "정지 (60초)" && !Get<bool>(f, "waiting"), "interval mode: a 169.254-only reading (DHCP not answering) keeps the schedule too");
    Check(Get<bool>(f, "intervalRun"), "the running session remembers its mode (intervalRun)");
    Check(!(bool)Call(f, "TrackWaiting", true, Environment.TickCount - 100000, true) && btn.Text == "정지 (60초)" && !Get<bool>(f, "waiting"), "interval mode: the wait-timeout change never fires and never paints IP 대기 digits");
    now = Environment.TickCount; seen.Clear();
    for (int s = 3; s >= 1; s--) { Set(f, "autoDueTick", unchecked(now + s * 1000 - 1)); Call(f, "RunAutoIfDue"); seen.Add(btn.Text); }
    Check(seen[0] == "정지 (3초)" && seen[1] == "정지 (2초)" && seen[2] == "정지 (1초)", "interval countdown digits: " + string.Join(" ", seen.ToArray()));
    Set(f, "autoDueTick", dueInt);
    Set(f, "busy", true);
    Set(f, "autoDueTick", unchecked(Environment.TickCount - 1));
    Call(f, "RunAutoIfDue");
    Check(Get<bool>(f, "autoPending") && !Get<System.ComponentModel.BackgroundWorker>(f, "worker").IsBusy, "a due change waits while an operation is still running (no overlap)");
    Set(f, "busy", false);
    // completion while running in interval mode re-arms the countdown on the fixed grid: previous due + N (the change itself took time, so less than N remains)
    int prevDue = unchecked(Environment.TickCount - 7000);   // the change was due 7 s ago and has just finished
    Set(f, "autoDueTick", prevDue); Set(f, "autoPending", false); btn.Text = "정지";
    Call(f, "worker_RunWorkerCompleted", null, new System.ComponentModel.RunWorkerCompletedEventArgs(res, null, false));
    remaining = unchecked(Get<int>(f, "autoDueTick") - Environment.TickCount);
    Check(Get<bool>(f, "autoRunning") && Get<bool>(f, "autoPending") && Get<int>(f, "autoDueTick") == unchecked(prevDue + 60000) && btn.Text == "정지 (53초)" && remaining > 52000 && remaining <= 53000 && t.Enabled && !g2.Enabled, "interval completion: next change at previous due + 60 s (53 s left after a 7 s change), countdown timer re-phased, graph timer untouched: " + btn.Text);
    t.Stop();
    Set(f, "autoDueTick", unchecked(Environment.TickCount - 70000)); Set(f, "autoPending", false); btn.Text = "정지";   // a change that took longer than the interval (its slot was 70 s ago)
    Call(f, "worker_RunWorkerCompleted", null, new System.ComponentModel.RunWorkerCompletedEventArgs(res, null, false));
    int catchUp = Get<int>(f, "autoDueTick");
    Check(Get<bool>(f, "autoPending") && btn.Text == "정지" && unchecked(catchUp - Environment.TickCount) <= 0 && unchecked(Environment.TickCount - catchUp) < 1000 && !t.Enabled, "interval completion after an over-long change: exactly one catch-up change due now (next tick), grid re-anchored at now, no timer re-phase, button 정지");
    Set(f, "autoPending", false); btn.Text = "정지";   // that catch-up change fires (autoDueTick stays at its slot) and completes 6 s later
    Set(f, "autoDueTick", unchecked(Environment.TickCount - 6000));
    Call(f, "worker_RunWorkerCompleted", null, new System.ComponentModel.RunWorkerCompletedEventArgs(res, null, false));
    Check(Get<bool>(f, "autoPending") && btn.Text == "정지 (54초)", "after the catch-up change the grid continues from it: next change 54 s later, not another immediate one: " + btn.Text);
    t.Stop();
    Call(f, "StopAuto");
    // 시작 pressed while a manual operation runs (interval mode): no countdown until the operation completes
    Call(f, "StopAuto"); t.Stop();
    Set(f, "busy", true);
    Call(f, "StartAuto");
    Check(Get<bool>(f, "autoRunning") && !Get<bool>(f, "autoPending") && btn.Text == "정지" && !t.Enabled && unchecked(Environment.TickCount - Get<int>(f, "autoDueTick")) < 1000, "interval 시작 during an operation: running, nothing scheduled and no countdown until it completes; the grid starts at the press");
    Set(f, "busy", false);
    Call(f, "worker_RunWorkerCompleted", null, new System.ComponentModel.RunWorkerCompletedEventArgs(res, null, false));
    Check(Get<bool>(f, "autoPending") && (btn.Text == "정지 (60초)" || btn.Text == "정지 (59초)"), "the completion of that operation arms the first interval countdown (60 s from the press): " + btn.Text);
    t.Stop();
    // a failed operation stops auto-change in interval mode too
    object fail = New("MacChanger.Core.MacChangeResult");
    SetProp(fail, "Success", false); SetProp(fail, "Message", "nope");
    f.Hide();
    Set(f, "autoPending", false); btn.Text = "정지";
    // (the failure path shows a MessageBox; call StopAuto directly instead and verify the state the handler would leave)
    Call(f, "StopAuto");
    Check(!Get<bool>(f, "autoRunning") && !Get<bool>(f, "autoPending") && btn.Text == "시작" && sw.Enabled && nudInt.Enabled && chk.Enabled && !nud.Enabled, "interval stop: unlocked (interval field + switch), IP fields stay locked while interval mode is selected");
    // stop prefix applies in interval mode as well
    stopChk.Checked = true; stopBox.Text = "123.123";
    Call(f, "StartAuto");
    Set(reading, "Addresses", "123.123.9.9");
    Call(f, "ApplyIpInfo", fakeGuid, reading, false, Get<int>(f, "ipEpoch"));
    Check(!Get<bool>(f, "autoRunning") && !Get<bool>(f, "autoPending") && btn.Text == "시작" && status.Text.Contains("123.123.9.9"), "interval mode: the stop prefix still stops auto-change the moment a matching IP is assigned: " + status.Text);
    stopChk.Checked = false;
    // no adapter selected (empty list): the due interval change stops auto-change with a message instead of a dead end
    Set(f, "suppressSelectionChanged", true); cbo.SelectedIndex = -1; Set(f, "suppressSelectionChanged", false);
    Call(f, "StartAuto"); t.Stop();
    Set(f, "autoDueTick", unchecked(Environment.TickCount - 1));
    Call(f, "RunAutoIfDue");
    Check(!Get<bool>(f, "autoRunning") && !Get<bool>(f, "autoPending") && btn.Text == "시작" && status.Text.Contains("어댑터를 선택"), "interval mode without an adapter: the due change stops auto-change and says so: " + status.Text);
    Set(f, "suppressSelectionChanged", true); cbo.SelectedItem = fake; Set(f, "suppressSelectionChanged", false);
    // mode switch back to IP mode restores the IP-mode gating (nothing leaks from the interval run)
    Call(f, "lblMode_Click", lblModeIp, EventArgs.Empty);
    Check(!(bool)GetProp(sw, "Checked") && nud.Enabled && waitNud.Enabled && !nudInt.Enabled && btn.Text == "시작", "back to IP 할당 시: delay/wait enabled, interval locked");
    Set(f, "suppressSelectionChanged", true); cbo.Items.Remove(fake); Set(f, "suppressSelectionChanged", false); Set(f, "waiting", false); Set(f, "lastLoggedIp", null);
    // ---- wait-timeout tracking (IP mode) ----
    t.Stop();   // the last StartAuto re-phased (started) the countdown timer; production never stops it, tests do
    waitNud.Value = 10; Set(f, "waiting", false); Set(f, "autoRunning", true); Set(f, "intervalRun", false);   // production sets both in StartAuto
    int t0 = 1000000;
    Check(!(bool)Call(f, "TrackWaiting", true, t0, true) && Get<bool>(f, "waiting") && t.Enabled && !g2.Enabled, "wait start: starts the clock, does not fire, re-phases the countdown timer, leaves the graph timer alone");
    t.Stop();
    Check(!(bool)Call(f, "TrackWaiting", true, t0 + 9999, true) && btn.Text.Contains("9"), "9.999 s later: still waiting, button shows elapsed: " + btn.Text);
    Check((bool)Call(f, "TrackWaiting", true, t0 + 10000, true) && !Get<bool>(f, "waiting"), "10 s reached: fires once and resets the clock");
    Check(!(bool)Call(f, "TrackWaiting", true, t0 + 10001, true), "the tick right after a fire starts a new clock (no double fire)");
    waitNud.Value = 60; Set(f, "waiting", false);
    Call(f, "TrackWaiting", true, t0, true);
    Check(!(bool)Call(f, "TrackWaiting", true, t0 + 59999, true) && (bool)Call(f, "TrackWaiting", true, t0 + 60000, true), "60 s setting: fires at exactly 60 s, not before");
    Set(f, "waiting", false); Call(f, "TrackWaiting", true, t0, true);
    Check(!(bool)Call(f, "TrackWaiting", false, t0 + 60000, true) && !Get<bool>(f, "waiting"), "an IP arriving clears the waiting state without firing");
    Set(f, "busy", true); Set(f, "waiting", false); Call(f, "TrackWaiting", true, t0, true);
    Check(!Get<bool>(f, "waiting") && !(bool)Call(f, "TrackWaiting", true, t0 + 60000, true), "while an operation runs the clock does not count");
    t.Stop(); Set(f, "busy", false); Set(f, "autoRunning", false); Set(f, "waiting", false); Call(f, "TrackWaiting", true, t0, true);
    Check(!(bool)Call(f, "TrackWaiting", true, t0 + 60000, true) && !t.Enabled, "auto-change not running: tracks but never fires and does not touch the timer");
    t.Stop(); Set(f, "waiting", false); waitNud.Value = 30; chk.Checked = false; stopChk.Checked = false;
    Check(!nud.Enabled && !waitNud.Enabled && !nudInt.Enabled && !btn.Enabled && sw.Enabled, "unticking 사용 locks delay, wait seconds, interval and 시작 again (the switch stays usable)");
    f.Dispose();
    Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
    return fails == 0 ? 0 : 1;
  }
}
