using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Commands; 
using CounterStrikeSharp.API.Modules.Utils;
using CSTimers = CounterStrikeSharp.API.Modules.Timers;
public class ShowDamagePlugin : BasePlugin
{
    public override string ModuleName => "ShowDamage by ABKAM";
    public override string ModuleVersion => "1.0.1";
    private Dictionary<CCSPlayerController, string> playerMessages = new Dictionary<CCSPlayerController, string>();
    private Dictionary<CCSPlayerController, CSTimers.Timer> playerMessageTimers = new Dictionary<CCSPlayerController, CSTimers.Timer>();
    private Dictionary<CCSPlayerController, int> grenadeDamage = new Dictionary<CCSPlayerController, int>();
    private Dictionary<CCSPlayerController, byte> playerTeams = new Dictionary<CCSPlayerController, byte>();
    private Dictionary<string, bool> showDamageEnabled = new Dictionary<string, bool>();
    private string showDamageFilePath;
    private Config config;
    private string configFilePath;
    private void LoadShowDamageConfig()
    {
        if (File.Exists(showDamageFilePath))
        {
            string json = File.ReadAllText(showDamageFilePath);
            showDamageEnabled = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json) ?? new Dictionary<string, bool>();
        }
        else
        {
            showDamageEnabled = new Dictionary<string, bool>();
        }
    }
    private void SaveShowDamageConfig()
    {
        string json = JsonConvert.SerializeObject(showDamageEnabled);
        File.WriteAllText(showDamageFilePath, json);
    }
    private void LoadConfig()
    {
        if (File.Exists(configFilePath))
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            string yml = File.ReadAllText(configFilePath);
            config = deserializer.Deserialize<Config>(yml) ?? new Config();
        }
        else
        {
            config = new Config();
            SaveConfig();
        }
    }
    private void SaveConfig()
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        string yml = serializer.Serialize(config);
        File.WriteAllText(configFilePath, yml);
    }    
    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        showDamageFilePath = Path.Combine(ModuleDirectory, "commandsave.json"); 
        configFilePath = Path.Combine(ModuleDirectory, "Config.yml");
        LoadShowDamageConfig(); 
        LoadConfig();      
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
    }
    [ConsoleCommand("damage", "Переключить отображение урона")]
    public void ToggleShowDamageCommand(CCSPlayerController player, CommandInfo command)
    {
        ToggleShowDamage(player);
    }
    private void ToggleShowDamage(CCSPlayerController player)
    {
        string playerSteamId = player.SteamID.ToString();

        if (showDamageEnabled.ContainsKey(playerSteamId))
        {
            showDamageEnabled.Remove(playerSteamId);
            string enabledMessage = ReplaceColorPlaceholders(config.ShowDamageEnabledMessage);
            player.PrintToChat(enabledMessage);
        }
        else
        {
            showDamageEnabled[playerSteamId] = true;
            string disabledMessage = ReplaceColorPlaceholders(config.ShowDamageDisabledMessage);
            player.PrintToChat(disabledMessage);
        }

        SaveShowDamageConfig();
    }
    private HookResult OnPlayerTeam(EventPlayerTeam eventInfo, GameEventInfo gameEventInfo)
    {
        if (eventInfo == null) return HookResult.Continue;

        var player = eventInfo.Userid; 
        var newTeam = (byte)eventInfo.Team;

        playerTeams[player] = newTeam;

        return HookResult.Continue;
    }
    private HookResult OnPlayerHurt(EventPlayerHurt eventInfo, GameEventInfo gameEventInfo)
    {
        if (eventInfo == null) return HookResult.Continue;

        var attacker = eventInfo.Attacker; 
        var victim = eventInfo.Userid; 

        if (attacker != null && victim != null && playerTeams.TryGetValue(attacker, out var attackerTeam) && playerTeams.TryGetValue(victim, out var victimTeam))
        {
            if (attackerTeam != victimTeam)
            {
                var remainingHP = eventInfo.Health; 
                var damageHP = eventInfo.DmgHealth;
                var hitgroup = eventInfo.Hitgroup;

                string attackerSteamId = attacker.SteamID.ToString();
                if (!showDamageEnabled.ContainsKey(attackerSteamId)) 
                {
                    if (eventInfo.Weapon == "hegrenade")
                    {
                        if (!grenadeDamage.ContainsKey(attacker))
                        {
                            grenadeDamage[attacker] = 0;
                        }
                        grenadeDamage[attacker] += damageHP;

                        ShowTotalGrenadeDamage(attacker);
                    }
                    else
                    {
                        var message = string.Format(config.DamageMessage, damageHP, remainingHP, HitGroupToString(hitgroup));
                        UpdateCenterMessage(attacker, message, 2);
                    }
                }
            }
        }

        return HookResult.Continue;
    }
    private void ShowTotalGrenadeDamage(CCSPlayerController attacker)
    {
        if (grenadeDamage.TryGetValue(attacker, out var totalDamage))
        {
            var message = string.Format(config.GrenadeDamageMessage, totalDamage);

            playerMessages[attacker] = message;

            if (playerMessageTimers.TryGetValue(attacker, out var existingTimer))
            {
                existingTimer.Kill(); 
            }

            var messageTimer = AddTimer(2.0f, () => 
            {
                playerMessages.Remove(attacker);
            });

            playerMessageTimers[attacker] = messageTimer;
        }
    }
    private void UpdateCenterMessage(CCSPlayerController player, string message, float durationInSeconds)
    {
        playerMessages[player] = message;

        if (playerMessageTimers.TryGetValue(player, out var existingTimer))
        {
            existingTimer.Kill(); 
        }

        var messageTimer = AddTimer(durationInSeconds, () =>
        {
            playerMessages.Remove(player);
        });

        playerMessageTimers[player] = messageTimer;
    }
    private void OnTick()
    {
        foreach (var kvp in playerMessages)
        {
            var player = kvp.Key;
            var message = kvp.Value;

            if (player != null && player.IsValid)
            {
                player.PrintToCenterHtml(message);
            }
        }
    }
    private string HitGroupToString(int hitGroup)
    {
        switch (hitGroup)
        {
            case 1:
                return "Голова";
            case 2:
                return "Грудь";
            case 3:
                return "Живот";
            case 4:
                return "Левая рука";
            case 5:
                return "Правая рука";
            case 6:
                return "Левая нога";
            case 7:
                return "Правая нога";
            case 10:
                return "Экипировка";
            default:
                return "Неизвестно";
        }
    }
    private string ReplaceColorPlaceholders(string message)
    {
        if (message.Contains('{'))
        {
            string modifiedValue = message;
            foreach (FieldInfo field in typeof(ChatColors).GetFields())
            {
                string pattern = $"{{{field.Name}}}";
                if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    modifiedValue = modifiedValue.Replace(pattern, field.GetValue(null).ToString(), StringComparison.OrdinalIgnoreCase);
                }
            }
            return modifiedValue;
        }

        return message;
    }    
    public class Config
    {
        public string GrenadeDamageMessage { get; set; } = "Общий урон от гранаты: <font color='red'>{0}❤<</font>";
        public string DamageMessage { get; set; } = "Урон: <font color='red'>{0}♥</font>, Остаток HP: <font color='green'>{1}❤</font>, Попадание: <font color='yellow'>{2}</font>";
        public string ShowDamageEnabledMessage { get; set; } = "[ {Green}ShowDamage{White} ] Отображение урона {Green}включено{White}.";
        public string ShowDamageDisabledMessage { get; set; } = "[ {Red}ShowDamage{White} ] Отображение урона {Red}отключено{White}.";
    }

}
