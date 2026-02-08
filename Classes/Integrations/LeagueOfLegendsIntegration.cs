using RePlays.Services;
using RePlays.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;


namespace RePlays.Integrations {
    internal class LeagueOfLegendsIntegration : Integration {
        static Timer timer;

        public PlayerStats stats;
        public List<int> trackedEvents;

        private JsonElement? getPlayer(JsonElement allPlayers, JsonElement ev) {
            if (allPlayers
                     .EnumerateArray()
                     .Any(playerElement => {

                         return playerElement.GetProperty("riotIdGameName").GetString() == ev.GetProperty("KillerName").ToString() || (playerElement.GetProperty("championName").GetString() + " Bot") == ev.GetProperty("KillerName").ToString();
                     })) {
                return allPlayers
                     .EnumerateArray()
                     .FirstOrDefault(playerElement => {

                         return playerElement.GetProperty("riotIdGameName").GetString() == ev.GetProperty("KillerName").ToString() || (playerElement.GetProperty("championName").GetString() + " Bot") == ev.GetProperty("KillerName").ToString();
                     });
            }
            return null;
        }
        private string getChampName(JsonElement elem) {
            return elem.GetProperty("rawChampionName").GetString().Replace("game_character_displayname_", "").Replace("FiddleSticks", "Fiddlesticks");
        }
        public override Task Start() {
            Logger.WriteLine("Starting League Of Legends integration");
            stats = new PlayerStats();
            trackedEvents = new List<int>();
            timer = new() {
                Interval = 250,
            };

            timer.Elapsed += async (sender, e) => {
                using var handler = new HttpClientHandler {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
                string result = "";
                using HttpClient client = new(handler);
                try {
                    result = await client.GetStringAsync("https://127.0.0.1:2999/liveclientdata/allgamedata");
                    JsonDocument doc = JsonDocument.Parse(result);
                    JsonElement root = doc.RootElement;

                    if (!root.TryGetProperty("events", out JsonElement eventList)) {
                        return;
                    }
                    else {
                        if (!eventList.TryGetProperty("Events", out JsonElement events)) {
                            return;
                        }
                        else {
                            if (!events.EnumerateArray().Any(
                                element => element.TryGetProperty("EventName", out JsonElement propertyValue) &&
                                propertyValue.GetString() == "GameStart")) {
                                return;
                            }
                        }
                    }

                    string username = "";

                    // lulzsun: now, i no longer play LOL so i am not exactly sure of this,
                    // but how come api documentation sample does not use 'riotId'? is it wrong or 
                    // is it not updated?
                    // https://static.developer.riotgames.com/docs/lol/liveclientdata_sample.json
                    // just incase, we will fallback to using the 'summonerName' property if it fails
                    if (root.TryGetProperty("activePlayer", out JsonElement activePlayer) &&
                        activePlayer.TryGetProperty("riotId", out JsonElement id)) {
                        username = id.GetString();
                    }
                    else if (root.TryGetProperty("activePlayer", out activePlayer) &&
                        activePlayer.TryGetProperty("summonerName", out id)) {
                        username = id.GetString();
                    }

                    // Parsing all players
                    JsonElement allPlayers = root.GetProperty("allPlayers");
                    JsonElement currentPlayer = allPlayers
                        .EnumerateArray()
                        .FirstOrDefault(playerElement => {
                            // lulzsun: same issue from above applies here...
                            if (playerElement.TryGetProperty("riotId", out JsonElement id)) {
                                return id.GetString() == username;
                            }
                            return playerElement.GetProperty("summonerName").GetString() == username;
                        });

                    int currentKills = currentPlayer.GetProperty("scores").GetProperty("kills").GetInt32();
                    int currentDeaths = currentPlayer.GetProperty("scores").GetProperty("deaths").GetInt32();
                    int currentAssists = currentPlayer.GetProperty("scores").GetProperty("assists").GetInt32();
                    var gameEvents = root.GetProperty("events").GetProperty("Events").EnumerateArray().Where(element => !trackedEvents.Contains(element.GetProperty("EventID").GetInt32())).ToList();

                    if (gameEvents.Count > 0) {
                        foreach (var ev in gameEvents) {
                            var eventName = ev.GetProperty("EventName").GetString();
                            if (eventName == "ChampionKill") {
                                JsonElement? killer = getPlayer(allPlayers, ev);
                                if (killer != null) {
                                    var victim = allPlayers
                                                    .EnumerateArray()
                                                    .FirstOrDefault(playerElement => {

                                                        return playerElement.GetProperty("riotIdGameName").GetString() == ev.GetProperty("VictimName").ToString()
                                                        || (playerElement.GetProperty("championName").GetString() + " Bot") == ev.GetProperty("VictimName").ToString();
                                                    });
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Kill, meta = new LeagueMeta { team = killer?.GetProperty("team").ToString() == "ORDER" ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer), victimChamp = getChampName(victim) } });
                                    trackedEvents.Add(ev.GetProperty("EventID").GetInt32());
                                }
                            }
                            else if (eventName == "TurretKilled") {
                                var killer = getPlayer(allPlayers, ev);

                                if (killer != null) {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Turret, meta = new LeagueMeta { team = ev.GetProperty("TurretKilled").ToString().StartsWith("Turret_TChaos") ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer) } });
                                }
                                else {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Turret, meta = new LeagueMeta { team = ev.GetProperty("TurretKilled").ToString().StartsWith("Turrt_TChaos") ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red } });

                                }
                                trackedEvents.Add(ev.GetProperty("EventID").GetInt32());
                            }
                            else if (eventName == "InhibKilled") {
                                var killer = getPlayer(allPlayers, ev);

                                if (killer != null) {

                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Inhib, meta = new LeagueMeta { team = ev.GetProperty("InhibKilled").ToString().StartsWith("Inhib_TChaos") ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer) } });

                                }
                                else {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Inhib, meta = new LeagueMeta { team = ev.GetProperty("InhibKilled").ToString().StartsWith("Inhib_TChaos") ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red } });

                                }
                                trackedEvents.Add(ev.GetProperty("EventID").GetInt32());

                            }
                            else if (eventName == "DragonKill") {
                                var killer = getPlayer(allPlayers, ev);
                                if (killer != null) {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Dragon, meta = new LeagueMeta { team = killer?.GetProperty("team").ToString() == "ORDER" ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer) } });
                                }
                                trackedEvents.Add(ev.GetProperty("EventID").GetInt32());
                            }
                            else if (eventName == "BaronKill") {
                                var killer = getPlayer(allPlayers, ev);
                                if (killer != null) {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Baron, meta = new LeagueMeta { team = killer?.GetProperty("team").ToString() == "ORDER" ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer) } });
                                }
                                trackedEvents.Add(ev.GetProperty("EventID").GetInt32());
                            }
                            else if (eventName == "HeraldKill") {
                                var killer = getPlayer(allPlayers, ev);
                                if (killer != null) {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.Herald, meta = new LeagueMeta { team = killer?.GetProperty("team").ToString() == "ORDER" ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer) } });
                                }
                                trackedEvents.Add(ev.GetProperty("EventID").GetInt32());
                            }
                            else if (eventName == "HordeKill") {
                                var killer = getPlayer(allPlayers, ev);
                                if (killer != null) {
                                    BookmarkService.AddBookmark(new Bookmark { type = Bookmark.BookmarkType.VoidGrubs, meta = new LeagueMeta { team = killer?.GetProperty("team").ToString() == "ORDER" ? LeagueMeta.TeamType.Blue : LeagueMeta.TeamType.Red, killerName = getChampName((JsonElement)killer) } });
                                }
                                trackedEvents.Add(ev.GetProperty("EventID").GetInt32());
                            }
                        }
                    }

                    stats.Kills = currentKills;
                    stats.Deaths = currentDeaths;
                    stats.Assists = currentAssists;
                    stats.Champion = currentPlayer.GetProperty("rawChampionName").GetString().Replace("game_character_displayname_", "").Replace("FiddleSticks", "Fiddlesticks");
                    stats.Win = root.GetProperty("events").GetProperty("Events")
                        .EnumerateArray()
                        .Where(eventElement => eventElement.GetProperty("EventName").GetString() == "GameEnd")
                        .Select(eventElement => eventElement.GetProperty("Result").GetString() switch {
                            "Win" => true,
                            "Lose" => false,
                            _ => (bool?)null
                        })
                        .FirstOrDefault();
                }
                catch (Exception ex) {
                    if (ex.GetType() != typeof(HttpRequestException)) {
                        Logger.WriteLine(ex.ToString());
                        Logger.WriteLine("Provided json: " + Regex.Replace(result, @"\n|\r\n", ""));
                        // just shutdown at this point, its probably broken
                        await Shutdown();
                    }
                }
            };
            timer.Start();
            Logger.WriteLine("Successfully started League Of Legends integration");
            return Task.CompletedTask;
        }

        public override Task Shutdown() {
            if (timer.Enabled) {
                timer.Stop();
                timer.Dispose();
                Logger.WriteLine("Shutting down League Of Legends integration");
            }
            else {
                Logger.WriteLine("Already shutdown League Of Legends integration!");
            }
            return Task.CompletedTask;
        }

        public void UpdateMetadataWithStats(string videoPath) {
            string thumbsDir = Path.Combine(Path.GetDirectoryName(videoPath), ".thumbs/");
            string metadataPath = Path.Combine(thumbsDir, Path.GetFileNameWithoutExtension(videoPath) + ".metadata");
            if (File.Exists(metadataPath)) {
                VideoMetadata metadata = JsonSerializer.Deserialize<VideoMetadata>(File.ReadAllText(metadataPath));
                metadata.kills = stats.Kills;
                metadata.assists = stats.Assists;
                metadata.deaths = stats.Deaths;
                metadata.champion = stats.Champion;
                metadata.win = stats.Win;
                File.WriteAllText(metadataPath, JsonSerializer.Serialize<VideoMetadata>(metadata));
            }
        }
    }

    public class PlayerStats {
        public int Kills { get; set; }
        public int Assists { get; set; }
        public int Deaths { get; set; }
        public string Champion { get; set; }
        public bool? Win { get; set; }
    }
}