using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Menu;
using Dapper;
using IksAdminApi;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using VipCoreApi;

namespace IksAdmin_VipManage;

public class VipModel {
	public long AccountId {get; set;}
	public string Group {get; set;}
	public long Expires {get; set;}

	public VipModel(long accountId, string group, long expires) {
		AccountId = accountId;
		Group = group;
		Expires = expires;
	}
}

public class Main : AdminModule
{
    public override string ModuleName => "IksAdmin_VipManage";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "iks__";
    public override string ModuleDescription => "For IksAdmin 3.0";
    private string _dbString = "";
    public static PluginConfig Config = null!;

	public static VipModel[] _playerVipGroup = new VipModel[72]; 

	private static PluginCapability<IVipCoreApi> _vipApiCapability = new("vipcore:core");
    public static IVipCoreApi? VipApi;

    public override void Ready()
    {
        base.Ready();
        PluginConfig.Set();
        Config = PluginConfig.Config;
        Api.MenuOpenPre += OnMenuOpenPre;
        var builder = new MySqlConnectionStringBuilder();
        builder.Server = Config.Host;
        builder.UserID = Config.User;
        builder.Password = Config.Pass;
        builder.Port = Config.Port;
        builder.Database = Config.Database;
        _dbString = builder.ToString();
		Api.RegisterPermission("other.vip_manage", "z");
    }
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        base.OnAllPluginsLoaded(hotReload);
		try
        {
            VipApi = _vipApiCapability.Get();
        }
        catch (Exception _)
        {
            AdminUtils.LogDebug("Start without Vip(Cs#)");
        }
    }
	[GameEventHandler]
	public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
	{
		var player = @event.Userid;
		if (player == null || player.AuthorizedSteamID == null) return HookResult.Continue;

		var accountId = player.AuthorizedSteamID.AccountId;
		var slot = player.Slot;
		Task.Run(async () => {
			_playerVipGroup[slot] = await GetPlayerVipGroup(accountId);
		});

		return HookResult.Continue;
	}
	

    private HookResult OnMenuOpenPre(CCSPlayerController player, IDynamicMenu menu, IMenu gameMenu)
    {
        if (menu.Id != "iksadmin:menu:main") return HookResult.Continue;
        
		menu.AddMenuOption("vip_manage", Localizer["MenuOption.VipManage"], (_, _) => {
			OpenVipManageMenu(player, menu);
		}, viewFlags: AdminUtils.GetCurrentPermissionFlags("other.vip_manage"));

        return HookResult.Continue;
    }

    private void OpenVipManageMenu(CCSPlayerController caller, IDynamicMenu? backMenu = null!)
    {
        var menu = Api.CreateMenu(
			"vip_manage.main",
			Localizer["MenuTitle.VipManage"]
		);

		menu.AddMenuOption("give", Localizer["MenuOption.Give"], (_, _) => {

		});

		menu.AddMenuOption("edit", Localizer["MenuOption.Edit"], (_, _) => {

		});

		menu.AddMenuOption("delete", Localizer["MenuOption.Delete"], (_, _) => {

		});

		menu.Open(caller);
    }

    [GameEventHandler]
	public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
		if (@event.Userid == null) return HookResult.Continue;
		int slot = @event.Userid.Slot;
		_playerVipGroup[slot] = new VipModel(0, "", -1);
		return HookResult.Continue;
	}

	public static string PlayerVipGroup(CCSPlayerController player)
    {
        try
        {
            if (VipApi != null)
            {
                if (VipApi.IsClientVip(player))
                    return VipApi!.GetClientVipGroup(player);
            }
            if (_playerVipGroup[player.Slot].Expires > AdminUtils.CurrentTimestamp() || _playerVipGroup[player.Slot].Expires == 0)
                return _playerVipGroup[player.Slot].Group;
            return "";
        }
        catch (System.Exception)
        {
            return "";
        }
    }
	public static string[] VipGroups() {
		if (VipApi != null)
		{
			return VipApi.GetVipGroups();
		} 
		return Config.Groups;
	}

    public async Task<VipModel> GetPlayerVipGroup(int accountId)
    {
        try
		{
			MySqlConnection conn = new MySqlConnection(_dbString);
			await conn.OpenAsync();
			var group = await conn.QueryFirstOrDefaultAsync<VipModel>(@"
			select 
			group as group,
			account_id as accountId,
			expires as expires
			from `vip_users` where (`account_id` = @accountId) and (`expires` > @dateNow or `expires` = 0) and sid = @sid
			", new
			{
				accountId,
				dateNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
				sid = Config.Sid
			});
			if (group == null)
			{
				return new VipModel(accountId, "", -1);
			}
			return group;
		}
		catch (Exception ex)
		{
			Exception e = ex;
			Console.WriteLine(e);
			throw;
		}
    }
}
