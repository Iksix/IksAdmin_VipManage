using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using Dapper;
using IksAdminApi;
using Microsoft.Extensions.Options;
using MySqlConnector;
using VipCoreApi;

namespace IksAdmin_VipManage;

public class VipModel {
	public long AccountId {get; set;}
	public string Group {get; set;}
	public long Expires {get; set;}
	
	[JsonIgnore]
	public bool IsExpired {get => Expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds();} 

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
    public override string ModuleDescription => "For IksAdmin 3.0 | Sponsored by Armatura";
    private string _dbString = "";
    public static PluginConfig Config = null!;

	public static VipModel[] _playerVipGroup = new VipModel[72]; 

	private static PluginCapability<IVipCoreApi> _vipApiCapability = new("vipcore:core");
    public static IVipCoreApi? VipApi;
	private bool _vipSharp = false;
	public int ServerId;

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
		if (Config.VipByPisex) {
			ServerId = Config.Sid;
		} else {
			_vipSharp = true;
		}
		Api.RegisterPermission("other.vip_manage", "z");
    }
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        base.OnAllPluginsLoaded(hotReload);
		try
        {
            VipApi = _vipApiCapability.Get();
			ServerId = VipApi!.GetServerId();
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
			OpenSelectPlayerMenu(player, menu);
		}, viewFlags: AdminUtils.GetCurrentPermissionFlags("other.vip_manage"));

        return HookResult.Continue;
    }

    private void OpenVipManageMenu(CCSPlayerController caller, CCSPlayerController target, IDynamicMenu? backMenu = null!)
    {
        var menu = Api.CreateMenu(
			"vip_manage.main",
			Localizer["MenuTitle.VipManage"],
			backMenu: backMenu
		);

		var accountId = target.AuthorizedSteamID!.AccountId;
		var group = _playerVipGroup[target.Slot];
		var groups = _vipSharp ? VipApi!.GetVipGroups() : Config.Groups;

		menu.AddMenuOption("group:" + group.Group, Localizer["MenuOption.Group"].AReplace(["group"], [group.Group]), (_, _) => {
			MenuUtils.SelectItemDefault<string?>(caller, "vm_group", groups.ToList()!, (g, mn) => {
				mn.BackAction = caller => {
					OpenVipManageMenu(caller, target, menu);
				};
				_playerVipGroup[target.Slot].Group = g!;
				OpenVipManageMenu(caller, target, menu);
				Task.Run(async () => {
					await UpdateVip(accountId, group);
					Server.NextFrame(() => {
						UpdateVipOnServer(target);
					});
				});
			});
		});

		menu.AddMenuOption("expires:" + group.Expires, Localizer["MenuOption.Expires"].AReplace(["date"], [group.Expires != -1 ? Utils.GetDateString((int)group.Expires) : "NONE"]), (_, _) => {
			caller.Print(Localizer["Message.WriteTime"]);
			Api.HookNextPlayerMessage(caller, s => {
				var fs = s.ToCharArray()[0];
				switch (fs)
				{
					case '+':
						group.Expires += long.Parse(s.Remove(0, 1))*60;
						break;
					case '-':
						group.Expires -= long.Parse(s.Remove(0, 1))*60;
						break;
					default:
						group.Expires = long.Parse(s)*60;
						break;
				}
				OpenVipManageMenu(caller, target, menu);
				Task.Run(async () => {
					await UpdateVip(accountId, group);
					Server.NextFrame(() => {
						UpdateVipOnServer(target);
					});
				});
			});
		}, disabled: group.Group == "");

		menu.Open(caller);
    }

    private void UpdateVipOnServer(CCSPlayerController target)
    {
        if (_vipSharp)
		{
			Server.ExecuteCommand("css_reload_vip_player " + target.GetSteamId());
		} else {
			Server.ExecuteCommand("mm_reload_vip " + target.AuthorizedSteamID!.AccountId);
		}
    }

    private void OpenSelectPlayerMenu(CCSPlayerController caller, IDynamicMenu? backMenu = null!)
    {
        var menu = Api.CreateMenu(
			"vip_manage.sp",
			Localizer["MenuOption.Give"],
			backMenu: backMenu
		);
		var players = PlayersUtils.GetOnlinePlayers().Where(x => _playerVipGroup[x.Slot].Expires != -1).ToList();

		foreach (var p in players)
		{
			menu.AddMenuOption("edit", Localizer["MenuOption.Edit"], (_, _) => {
				if (_playerVipGroup[p.Slot].Expires != -1)
				{
					// Редактирование вип пользователя
					OpenVipManageMenu(caller, p, menu);
				} else {
					// Добавление вип пользователя
				}
			});
		}
		
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
	public static string[] VipGroups() {
		if (VipApi != null)
		{
			return VipApi.GetVipGroups();
		} 
		return Config.Groups;
	}

    public async Task<VipModel> GetPlayerVipGroup(long accountId)
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
	public async Task UpdateVip(long accountId, VipModel vipModel)
    {
        try
		{
			MySqlConnection conn = new MySqlConnection(_dbString);
			await conn.OpenAsync();
			await conn.ExecuteAsync(@"
			UPDATE `vip_users` SET `group` = @group, `expires` = @expires WHERE `account_id` = @accountId and sid = @sid",
			new
			{
				group = vipModel.Group,
				expires = vipModel.Expires,
				accountId,
				sid = ServerId
			});
		}
		catch (Exception ex)
		{
			Exception e = ex;
			Console.WriteLine(e);
			throw;
		}
    }
}
