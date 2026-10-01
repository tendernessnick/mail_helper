using FlaUI.Core;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;

// S14 阅读窗格实测探针：点邮件列表第 N 项（默认 1=第二封，含 HTML 正文），停留供截图
int index = args.Length > 0 ? int.Parse(args[0]) : 1;
using var automation = new UIA3Automation();
var desktop = automation.GetDesktop();
var win = desktop.FindFirstDescendant(cf => cf.ByName("MailHelper"))
    ?? desktop.FindFirstDescendant(cf => cf.ByClassName("HwndWrapper[MailHelper]"));
if (win is null) { Console.WriteLine("WINDOW_NOT_FOUND"); return 1; }
var list = win.FindFirstDescendant(cf => cf.ByName("邮件列表"));
if (list is null) { Console.WriteLine("LIST_NOT_FOUND"); return 1; }
var items = list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
Console.WriteLine($"items={items.Length}");
if (items.Length <= index) { Console.WriteLine("NO_ITEM"); return 1; }
items[index].Click(false);
Console.WriteLine("clicked");
return 0;
