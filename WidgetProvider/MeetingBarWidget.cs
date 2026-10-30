using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets.Providers;
using Windows.Storage;
using MeetingBar.Localization;
using static MeetingBar.Localization.Localizer;
namespace MeetingBar.WidgetProvider;
internal sealed class MeetingBarWidget : IDisposable
{
    public const string DefinitionId="MeetingBar_Agenda"; private static string? _template; private readonly string _id; private readonly Timer _timer; private bool _active; private DateTime _lastWrite;
    public MeetingBarWidget(string id){_id=id;_timer=new Timer(_=>Update(false),null,Timeout.Infinite,Timeout.Infinite);}
    public void Activate(){_active=true;Update(true);_timer.Change(TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(15));}
    public void Deactivate(){_active=false;_timer.Change(Timeout.Infinite,Timeout.Infinite);}
    public void Update(bool force){try{var write=File.Exists(StatePath)?File.GetLastWriteTimeUtc(StatePath):DateTime.MinValue;if(!force&&write==_lastWrite&&!_active)return;_lastWrite=write;var snapshot=WidgetSnapshot.Load(StatePath);WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(_id){Template=GetTemplate(),Data=snapshot.ToWidgetData(),CustomState="v1"});}catch{}}
    public void Invoke(string verb){try{var state=WidgetSnapshot.Load(StatePath);if(string.IsNullOrWhiteSpace(state.ApplicationPath)||!File.Exists(state.ApplicationPath))return;var arg=verb switch{"join"=>"--join","refresh"=>"--refresh",_=>"--popup"};Process.Start(new ProcessStartInfo(state.ApplicationPath,arg){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(state.ApplicationPath)});}catch{}}
    public void Dispose()=>_timer.Dispose();
    private static string StatePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"MeetingBar","winw-widget-state.json");
    private static string GetTemplate(){if(!string.IsNullOrEmpty(_template))return _template;var file=StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Templates/MeetingBarWidget.json")).AsTask().GetAwaiter().GetResult();_template=FileIO.ReadTextAsync(file).AsTask().GetAwaiter().GetResult();return _template;}
}
internal sealed class WidgetSnapshot
{
    public string ApplicationPath{get;set;}=""; public bool Use24HourClock{get;set;}=true; public string Language{get;set;}=""; public List<WidgetEvent> Events{get;set;}=new();
    public static WidgetSnapshot Load(string path){try{if(File.Exists(path))return JsonSerializer.Deserialize<WidgetSnapshot>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new();}catch{}return new();}
    public string ToWidgetData(){Localizer.SetLanguage(Language);var now=DateTime.Now;var relevant=Events.Where(x=>x.End>now.AddHours(-24)).OrderBy(x=>x.Start).ToList();var focus=relevant.FirstOrDefault(x=>x.End>now&&!x.IsDismissed);var rows=relevant.Where(x=>x.Start.Date==now.Date).Take(7).Select(x=>new JsonObject{{"time",x.IsAllDay?T("All day"):x.Start.ToString(Use24HourClock?"HH:mm":"h:mm tt")},{"title",x.Title},{"status",x.IsDismissed?T("Muted"):Status(x,now)},{"isPast",x.End<=now},{"color",x.End<=now?"default":ColorToken(x.Color)}}).ToArray();var data=new JsonObject{{"header",focus==null?"MEETINGBAR":focus.Start<=now?T("CURRENT MEETING"):T("NEXT MEETING")},{"title",focus?.Title??T("No upcoming meetings")},{"meta",focus==null?T("Calendar is clear"):(focus.IsAllDay?T("All day"):focus.Start.ToString(Use24HourClock?"HH:mm":"h:mm tt")+" – "+focus.End.ToString(Use24HourClock?"HH:mm":"h:mm tt"))+"  ·  "+Status(focus,now)},{"color",ColorToken(focus?.Color)},{"events",new JsonArray(rows)},{"showEmpty",rows.Length==0},{"emptyMessage",rows.Length==0?T("No events today"):""},{"canJoin",focus?.CanJoin==true},{"joinLabel",T("Join")},{"openLabel",T("Open MeetingBar")},{"refreshLabel",T("Refresh")}};return data.ToJsonString();}
    private static string Status(WidgetEvent item,DateTime now){if(item.End<=now)return T("Ended");if(item.Start<=now){var left=item.End-now;return left.TotalHours<1?T("{0}m left",Math.Ceiling(left.TotalMinutes)):T("{0}h {1}m left",Math.Floor(left.TotalHours),left.Minutes);}var until=item.Start-now;if(until.TotalMinutes<1)return T("Soon");if(until.TotalHours<1)return T("in {0}m",Math.Ceiling(until.TotalMinutes));if(until.TotalDays<1)return T("in {0}h {1}m",Math.Floor(until.TotalHours),until.Minutes);return T("in {0}d",Math.Floor(until.TotalDays));}
    private static string ColorToken(string? value){if(string.IsNullOrWhiteSpace(value)||value.Length!=7||value[0]!='#')return"accent";try{var r=Convert.ToInt32(value.Substring(1,2),16);var g=Convert.ToInt32(value.Substring(3,2),16);var b=Convert.ToInt32(value.Substring(5,2),16);if(r>190&&g<130)return"attention";if(r>175&&g>130&&b<110)return"warning";if(g>r+30&&g>b)return"good";}catch{}return"accent";}
}
internal sealed class WidgetEvent{public string Title{get;set;}="Untitled event";public DateTime Start{get;set;}public DateTime End{get;set;}public bool IsAllDay{get;set;}public bool CanJoin{get;set;}public bool IsDismissed{get;set;}public string Color{get;set;}="#6D7CFF";}
