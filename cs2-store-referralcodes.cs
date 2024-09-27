using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using StoreApi;
using MySqlConnector;
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Menu;
using Menu.Enums;

namespace Store_Referral;

public class Store_ReferralConfig : BasePluginConfig
{
    [JsonPropertyName("referral_bonus")]
    public int ReferralBonus { get; set; } = 100;

    [JsonPropertyName("TopMenuType")]
    public int TopMenuType { get; set; } = 0;

    [JsonPropertyName("KitsuneMenuDeveloperDisplay")]
    public bool KitsuneMenuDeveloperDisplay { get; set; } = true;

    [JsonPropertyName("top_players_limit")]
    public int TopPlayersLimit { get; set; } = 10;

    [JsonPropertyName("referral_commands")]
    public List<string> ReferralCommands { get; set; } = ["referral", "useinvitecode"];

    [JsonPropertyName("generate_referral_commands")]
    public List<string> GenerateReferralCommands { get; set; } = ["generate_referral_code", "myreferral"];

    [JsonPropertyName("referral_count_commands")]
    public List<string> ReferralCount { get; set; } = ["myinvites", "invites"];

    [JsonPropertyName("top_referrals_commands")]
    public List<string> TopReferrals { get; set; } = ["topreferrals"];

    [JsonPropertyName("bonus_thresholds")]
    public Dictionary<int, int> BonusThresholds { get; set; } = new() { { 5, 1000 }, { 10, 2000 }, { 15, 3000 } };

    [JsonPropertyName("database_host")]
    public string DatabaseHost { get; set; } = "localhost";

    [JsonPropertyName("database_port")]
    public int DatabasePort { get; set; } = 3306;

    [JsonPropertyName("database_name")]
    public string DatabaseName { get; set; } = "name";

    [JsonPropertyName("database_user")]
    public string DatabaseUser { get; set; } = "root";

    [JsonPropertyName("database_password")]
    public string DatabasePassword { get; set; } = "password";
}

public class Store_Referral : BasePlugin, IPluginConfig<Store_ReferralConfig>
{
    public override string ModuleName => "Store Module [Referral Codes]";
    public override string ModuleVersion => "0.1.1";
    public override string ModuleAuthor => "Nathy";

    public IStoreApi? StoreApi { get; set; }
    public Store_ReferralConfig Config { get; set; } = new();

    public KitsuneMenu Menu { get; private set; } = null!;
    
    private void Menu_OnLoad()
    {
        Menu = new KitsuneMenu(this);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        StoreApi = IStoreApi.Capability.Get() ?? throw new Exception("StoreApi could not be located.");
        InitializeDatabase();
        CreateCommands();
        Menu_OnLoad();
    }

    public void OnConfigParsed(Store_ReferralConfig config)
    {
        Config = config;
    }

    private void CreateCommands()
    {
        foreach (var cmd in Config.ReferralCommands)
        {
            AddCommand($"css_{cmd}", "Use a referral code", Command_Referral);
        }

        foreach (var cmd in Config.GenerateReferralCommands)
        {
            AddCommand($"css_{cmd}", "Generate a referral code", Command_GenerateReferralCode);
        }

        foreach (var cmd in Config.ReferralCount)
        {
            AddCommand($"css_{cmd}", "Check how many times your referral code has been used", Command_CheckReferrals);
        }

        foreach (var cmd in Config.TopReferrals)
        {
            AddCommand($"css_{cmd}", "Show top 10 players with the most referrals", Command_TopReferrals);
        }
    }

    [CommandHelper(minArgs: 1, usage: "<code>")]
    public void Command_Referral(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        string referralCode = info.GetArg(1);
        string steamID = player.SteamID.ToString();

        using (var connection = new MySqlConnection(GetConnectionString()))
        {
            connection.Open();

            string checkCodeQuery = "SELECT OwnCode FROM store_referral_codes WHERE SteamID = @SteamID";
            using (var checkCodeCommand = new MySqlCommand(checkCodeQuery, connection))
            {
                checkCodeCommand.Parameters.AddWithValue("@SteamID", steamID);
                using (var reader = checkCodeCommand.ExecuteReader())
                {
                    if (!reader.Read() || reader.IsDBNull(reader.GetOrdinal("OwnCode")))
                    {
                        reader.Close();

                        string newCode = GenerateRandomCode();
                        reader.Close();
                        
                        string insertCodeQuery = "INSERT INTO store_referral_codes (SteamID, Name, OwnCode) VALUES (@SteamID, @Name, @OwnCode) ON DUPLICATE KEY UPDATE OwnCode = @OwnCode";
                        using (var insertCodeCommand = new MySqlCommand(insertCodeQuery, connection))
                        {
                            insertCodeCommand.Parameters.AddWithValue("@SteamID", steamID);
                            insertCodeCommand.Parameters.AddWithValue("@Name", player.PlayerName);
                            insertCodeCommand.Parameters.AddWithValue("@OwnCode", newCode);
                            insertCodeCommand.ExecuteNonQuery();
                        }
                    }
                }
            }

            string checkQuery = "SELECT COUNT(*) FROM store_referral_codes WHERE SteamID = @SteamID AND UsedCode IS NOT NULL";
            using (var checkCommand = new MySqlCommand(checkQuery, connection))
            {
                checkCommand.Parameters.AddWithValue("@SteamID", player.SteamID.ToString());

                int usedCount = Convert.ToInt32(checkCommand.ExecuteScalar());
                if (usedCount > 0)
                {
                    info.ReplyToCommand(Localizer["Prefix"] + Localizer["Already used referral code"]);
                    return;
                }
            }

            string query = "SELECT SteamID FROM store_referral_codes WHERE OwnCode = @OwnCode";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@OwnCode", referralCode);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string referrerSteamID = reader.GetUInt64("SteamID").ToString();

                        if (referrerSteamID == player.SteamID.ToString())
                        {
                            info.ReplyToCommand(Localizer["Prefix"] + Localizer["Cannot use own referral"]);
                            return;
                        }

                        reader.Close();

                        string getPlayerQuery = "SELECT Name, UsageCount FROM store_referral_codes WHERE SteamID = @SteamID";
                        using (var getPlayerCommand = new MySqlCommand(getPlayerQuery, connection))
                        {
                            getPlayerCommand.Parameters.AddWithValue("@SteamID", referrerSteamID);

                            using (var playerReader = getPlayerCommand.ExecuteReader())
                            {
                                if (playerReader.Read())
                                {
                                    string referrerName = playerReader.GetString("Name");
                                    int usageCount = playerReader.GetInt32("UsageCount");
                                    int referralBonus = Config.ReferralBonus;

                                    playerReader.Close();

                                    string updateQuery = "UPDATE store_referral_codes SET UsageCount = UsageCount + 1 WHERE SteamID = @SteamID";
                                    using (var updateCommand = new MySqlCommand(updateQuery, connection))
                                    {
                                        updateCommand.Parameters.AddWithValue("@SteamID", referrerSteamID);
                                        updateCommand.ExecuteNonQuery();
                                    }

                                    StoreApi.GivePlayerCredits(player, referralBonus);
                                    info.ReplyToCommand(Localizer["Prefix"] + Localizer["You used a referral code", referrerName, referralBonus]);

                                    usageCount += 1;
                                    bool bonusThresholdMet = Config.BonusThresholds.ContainsKey(usageCount);
                                    int thresholdBonus = bonusThresholdMet ? Config.BonusThresholds[usageCount] : 0;

                                    var referrerPlayer = Utilities.GetPlayers().FirstOrDefault(p => p.SteamID.ToString() == referrerSteamID);
                                    if (referrerPlayer != null)
                                    {
                                        StoreApi.GivePlayerCredits(referrerPlayer, referralBonus);

                                        if (bonusThresholdMet)
                                        {
                                            StoreApi.GivePlayerCredits(referrerPlayer, thresholdBonus);
                                        }
                                    }
                                    else
                                    {
                                        using (var storeConnection = new MySqlConnection(StoreApi.GetDatabaseString()))
                                        {
                                            storeConnection.Open();

                                            string updateCreditsQuery = "UPDATE store_players SET Credits = Credits + @Credits WHERE SteamID = @SteamID";
                                            using (var updateCreditsCommand = new MySqlCommand(updateCreditsQuery, storeConnection))
                                            {
                                                updateCreditsCommand.Parameters.AddWithValue("@Credits", referralBonus);
                                                updateCreditsCommand.Parameters.AddWithValue("@SteamID", referrerSteamID);
                                                updateCreditsCommand.ExecuteNonQuery();

                                                if (bonusThresholdMet)
                                                {
                                                    updateCreditsCommand.Parameters["@Credits"].Value = thresholdBonus;
                                                    updateCreditsCommand.ExecuteNonQuery();
                                                    referrerPlayer?.PrintToChat(Localizer["Prefix"] + Localizer["Threshold bonus", usageCount, thresholdBonus]);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        string updateUsedCodeQuery = "UPDATE store_referral_codes SET UsedCode = @UsedCode WHERE SteamID = @SteamID";
                        using (var updateUsedCodeCommand = new MySqlCommand(updateUsedCodeQuery, connection))
                        {
                            updateUsedCodeCommand.Parameters.AddWithValue("@UsedCode", referralCode);
                            updateUsedCodeCommand.Parameters.AddWithValue("@SteamID", player.SteamID.ToString());
                            updateUsedCodeCommand.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        info.ReplyToCommand(Localizer["Prefix"] + Localizer["Invalid referral code"]);
                    }
                }
            }
        }
    }

    public void Command_GenerateReferralCode(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        string steamID = player.SteamID.ToString();

        using (var connection = new MySqlConnection(GetConnectionString()))
        {
            connection.Open();

            string checkQuery = "SELECT OwnCode FROM store_referral_codes WHERE SteamID = @SteamID";
            using (var checkCommand = new MySqlCommand(checkQuery, connection))
            {
                checkCommand.Parameters.AddWithValue("@SteamID", steamID);

                using (var reader = checkCommand.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (!reader.IsDBNull(reader.GetOrdinal("OwnCode")))
                        {
                            string existingCode = reader.GetString("OwnCode");
                            player.PrintToChat(Localizer["Prefix"] + Localizer["Your referral code", existingCode]);
                            return;
                        }
                    }
                }
            }

            string code = GenerateRandomCode();

            string query = "INSERT INTO store_referral_codes (SteamID, Name, OwnCode) VALUES (@SteamID, @Name, @OwnCode) " +
                           "ON DUPLICATE KEY UPDATE OwnCode = @OwnCode";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@SteamID", steamID);
                command.Parameters.AddWithValue("@Name", player.PlayerName);
                command.Parameters.AddWithValue("@OwnCode", code);

                command.ExecuteNonQuery();
            }

            player.PrintToChat(Localizer["Prefix"] + Localizer["Your referral code", code]);
        }
    }

    public void Command_CheckReferrals(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        string steamID = player.SteamID.ToString();

        using (var connection = new MySqlConnection(GetConnectionString()))
        {
            connection.Open();

            string query = "SELECT UsageCount FROM store_referral_codes WHERE SteamID = @SteamID";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@SteamID", steamID);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        int usageCount = reader.GetInt32("UsageCount");
                        player.PrintToChat(Localizer["Prefix"] + Localizer["Invite count", usageCount]);
                    }
                    else
                    {
                        player.PrintToChat(Localizer["Prefix"] + Localizer["Dont have referral"]);
                    }
                }
            }
        }
    }

    public void Command_TopReferrals(CCSPlayerController? player, CommandInfo info) 
    {
        if (player == null) return;

        if (StoreApi == null) throw new Exception("StoreApi could not be located.");

        if (Config.TopMenuType == 0)
        {
            ShowTopReferralsChatMenu(player);
        }
        else if (Config.TopMenuType == 1)
        {
            ShowTopReferralsKitsuneMenu(player);
        }
    }

    private List<(string playerName, int usageCount)> FetchTopReferrals(int limit)
    {
        var referrals = new List<(string playerName, int usageCount)>();

        using (var connection = new MySqlConnection(GetConnectionString()))
        {
            connection.Open();

            string query = $@"
                SELECT Name, UsageCount
                FROM store_referral_codes
                ORDER BY UsageCount DESC
                LIMIT {limit}";

            using (var command = new MySqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string playerName = reader.GetString("Name");
                    int usageCount = reader.GetInt32("UsageCount");
                    referrals.Add((playerName, usageCount));
                }
            }
        }

        return referrals;
    }

    private void ShowTopReferralsChatMenu(CCSPlayerController player)
    {
        var topPlayers = FetchTopReferrals(Config.TopPlayersLimit);

        if (topPlayers.Count > 0)
        {
            player.PrintToChat(Localizer[$"Top {Config.TopPlayersLimit}"]);
            int rank = 1;

            foreach (var (playerName, usageCount) in topPlayers)
            {
                string message = Localizer["Top referrals", rank, playerName, usageCount];
                player.PrintToChat(message);
                rank++;
            }

            player.PrintToChat(Localizer[$"Top {Config.TopPlayersLimit} bottom"]);
        }
        else
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["No referrals data available"]);
        }
    }

    private void ShowTopReferralsKitsuneMenu(CCSPlayerController player)
    {
        if (Menu == null)
        {
            return;
        }

        string title = Localizer["Top Referrals"];
        List<MenuItem> items = new List<MenuItem>();
        var referralDictionary = new Dictionary<int, (string playerName, int usageCount)>();

        var topPlayers = FetchTopReferrals(Config.TopPlayersLimit);

        int rank = 1;
        foreach (var (playerName, usageCount) in topPlayers)
        {
            string message = Localizer["Top referrals", rank, playerName, usageCount];
            items.Add(new MenuItem(MenuItemType.Text, new MenuValue(message)));
            referralDictionary[rank] = (playerName, usageCount);
            rank++;
        }

        if (items.Count == 0)
        {
            player.PrintToChat(Localizer["Prefix"] + Localizer["No referrals data available"]);
            return;
        }

        Menu?.ShowScrollableMenu(player, title, items, (buttons, menu, selected) =>
        {
        }, false, freezePlayer: true, disableDeveloper: !Config.KitsuneMenuDeveloperDisplay);
    }

    private string GenerateRandomCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
          .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private string GetConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Config.DatabaseHost,
            Port = (uint)Config.DatabasePort,
            Database = Config.DatabaseName,
            UserID = Config.DatabaseUser,
            Password = Config.DatabasePassword
        };
        return builder.ConnectionString;
    }

    private void InitializeDatabase()
    {
        using (var connection = new MySqlConnection(GetConnectionString()))
        {
            connection.Open();

            string query = @"
                CREATE TABLE IF NOT EXISTS store_referral_codes (
                    SteamID BIGINT UNSIGNED NOT NULL,
                    Name VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci,
                    OwnCode VARCHAR(16),
                    UsedCode VARCHAR(16),
                    UsageCount INT NOT NULL DEFAULT 0,
                    PRIMARY KEY (SteamID)
                )";

            using (var command = new MySqlCommand(query, connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
