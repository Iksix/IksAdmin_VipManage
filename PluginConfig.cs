using IksAdminApi;

namespace IksAdmin_VipManage;

public class PluginConfig : PluginCFG<PluginConfig>
{
    public static PluginConfig Config = new PluginConfig();
    // Для VIP PISEXA
    public bool VipByPisex {get; set;} = false;
    public string Host { get; set; } = "host";
	public string Database { get; set; } = "db";
	public string User { get; set; } = "user";
	public string Pass { get; set; } = "pass";
	public uint Port { get; set; } = 3306;
    public string[] Groups {get; set;} = [ "VIP", "PREMIUM" ];
    // ===
	public int Sid { get; set; } = 0;

    public static void Set()
    {
        Config = Config.ReadOrCreate(Main.Instance.ModuleDirectory + "/../../configs/plugins/IksAdmin_Modules/VipManage.json", Config);
    }
}