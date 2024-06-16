using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopRegenArmor
{
    public class ShopRegenArmor : BasePlugin
    {
        public override string ModuleName => "[SHOP] Armor Regeneration";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IShopApi? SHOP_API;
        private const string CategoryName = "ArmorRegen";
        public static JObject? JsonArmorRegen { get; private set; }
        private readonly PlayerRegenArmor[] playerRegenArmors = new PlayerRegenArmor[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/ArmorRegen.json");
            if (File.Exists(configPath))
            {
                JsonArmorRegen = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonArmorRegen == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Регенерация брони");

            foreach (var item in JsonArmorRegen.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot =>
            {
                playerRegenArmors[playerSlot] = null!;
            });

            AddTimer(1.0f, Timer_ArmorRegen, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

            RegisterEventHandler<EventPlayerHurt>((@event, info) =>
            {
                var player = @event.Userid;

                if (player != null && playerRegenArmors[player.Slot] != null && @event.DmgArmor > 0)
                {
                    playerRegenArmors[player.Slot].IsRegenActive = true;
                }

                return HookResult.Continue;
            });
        }

        public void OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName,
            int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetRegenSettings(uniqueName, out var regenSettings))
            {
                playerRegenArmors[player.Slot] = new PlayerRegenArmor(regenSettings, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing settings in config!");
            }
        }

        public void OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetRegenSettings(uniqueName, out var regenSettings))
            {
                playerRegenArmors[player.Slot] = new PlayerRegenArmor(regenSettings, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
        }

        public void OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerRegenArmors[player.Slot] = null!;
        }

        private void Timer_ArmorRegen()
        {
            foreach (var player in Utilities.GetPlayers()
                         .Where(u => u.PlayerPawn.Value != null && u.PlayerPawn.Value.IsValid && u.PawnIsAlive))
            {
                if (playerRegenArmors[player.Slot] != null && playerRegenArmors[player.Slot] is var playerRegen && playerRegen.IsRegenActive)
                {
                    if (playerRegen.RegenSettings.Delay > 0)
                    {
                        playerRegen.RegenSettings.Delay--;
                        continue;
                    }

                    if (playerRegen.RegenInterval > 0)
                    {
                        playerRegen.RegenInterval--;
                        continue;
                    }

                    if (ArmorRegen(player, playerRegen.RegenSettings))
                    {
                        playerRegen.RegenInterval = playerRegen.RegenSettings.Interval;
                    }
                }
            }
        }

        private bool ArmorRegen(CCSPlayerController player, Regen regen)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) return true;

            if (playerPawn.ArmorValue < 100)
            {
                playerPawn.ArmorValue += regen.Armor;
                if (playerPawn.ArmorValue < 100)
                    return true;

                playerPawn.ArmorValue = 100;
            }

            playerRegenArmors[player.Slot].IsRegenActive = false;
            return false;
        }

        private static bool TryGetRegenSettings(string uniqueName, out Regen regenSettings)
        {
            regenSettings = new Regen();
            if (JsonArmorRegen != null && JsonArmorRegen.TryGetValue(uniqueName, out var obj) &&
                obj is JObject jsonItem && jsonItem["armor"] != null && jsonItem["armor"]!.Type != JTokenType.Null &&
                jsonItem["delay"] != null && jsonItem["delay"]!.Type != JTokenType.Null &&
                jsonItem["interval"] != null && jsonItem["interval"]!.Type != JTokenType.Null)
            {
                regenSettings.Armor = (int)jsonItem["armor"]!;
                regenSettings.Delay = (int)jsonItem["delay"]!;
                regenSettings.Interval = (int)jsonItem["interval"]!;
                return true;
            }

            return false;
        }

        public record class PlayerRegenArmor(Regen RegenSettings, int ItemID)
        {
            public bool IsRegenActive { get; set; } = false;
            public int RegenInterval { get; set; }
        }
    }

    public class Regen
    {
        public int Armor { get; set; } = 0;
        public int Delay { get; set; } = 0;
        public int Interval { get; set; } = 0;
    }
}