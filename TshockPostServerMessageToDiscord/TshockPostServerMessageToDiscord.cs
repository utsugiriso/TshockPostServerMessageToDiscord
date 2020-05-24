using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using Terraria.DataStructures;

using System.Net.Http;
using System.Net.Http.Headers;

using System.IO;
using System.Linq;

namespace TshockPostServerMessageToDiscord
{
    [ApiVersion(2, 1)]
    public class TshockPostServerMessageToDiscord : TerrariaPlugin
    {
        /// <summary>
        /// Gets the author(s) of this plugin
        /// </summary>
        public override string Author
        {
            get { return "utsugiriso"; }
        }

        /// <summary>
        /// Gets the description of this plugin.
        /// A short, one lined description that tells people what your plugin does.
        /// </summary>
        public override string Description
        {
            get { return "Post server message(login, chat, death, etc...) to discord channel."; }
        }

        /// <summary>
        /// Gets the name of this plugin.
        /// </summary>
        public override string Name
        {
            get { return "PostServerMessageToDiscord Plugin"; }
        }

        /// <summary>
        /// Gets the version of this plugin.
        /// </summary>
        public override Version Version
        {
            get { return new Version(4, 4, 0, 1); }
        }

        /// <summary>
        /// Initializes a new instance of the TestPlugin class.
        /// This is where you set the plugin's order and perfro other constructor logic
        /// </summary>
        public TshockPostServerMessageToDiscord(Main game) : base(game)
        {
        }

        /// <summary>
        /// Handles plugin initialization. 
        /// Fired when the server is started and the plugin is being loaded.
        /// You may register hooks, perform loading procedures etc here.
        /// </summary>
        public override void Initialize()
        {
            if (Configs == null)
                Configs = new Config();

            ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);
            //ServerApi.Hooks.ServerChat.Register(this, OnServerChat);
            TShockAPI.Hooks.PlayerHooks.PlayerChat += OnPlayerChat;
        }

        /// <summary>
        /// Handles plugin disposal logic.
        /// *Supposed* to fire when the server shuts down.
        /// You should deregister hooks and free all resources here.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                //ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
                TShockAPI.Hooks.PlayerHooks.PlayerChat -= OnPlayerChat;
            }
            base.Dispose(disposing);
        }

        public static Config Configs = null;
        public class Config
        {
            public const string DUMMY_DISCORD_WEBHOOK_URL = "https://discordapp.com/api/webhooks/hogehugahogehuga/hogehogehugahugehogeahogehuahugeaho";
            public string DiscordWebHookUrl = null;

            public Config()
            {
                string discordAppTokenPath = Path.Combine(TShock.SavePath, "discord_webhook_url.txt");
                DiscordWebHookUrl = Read(discordAppTokenPath, DUMMY_DISCORD_WEBHOOK_URL);
            }

            public string Read(string path, string defaultValue)
            {
                if (File.Exists(path))
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return Read(fs);
                    }
                }
                else
                {
                    Write(path, defaultValue);
                    return null;
                }
            }

            public string Read(Stream stream)
            {
                using (var sr = new StreamReader(stream))
                {
                    var cf = sr.ReadToEnd();
                    return cf;
                }
            }

            public void Write(string path, string defaultValue)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
                {
                    Write(fs, defaultValue);
                }
            }

            public void Write(Stream stream, string defaultValue)
            {
                using (var sw = new StreamWriter(stream))
                {
                    sw.Write(defaultValue);
                }
            }
        }

        protected string DecoratePlayerName(string playerName)
        {
            return $"{playerName}";
        }

        protected string CurrentPlayersMessage(List<TSPlayer> players)
        {
            return $"{players.Count} players joined {TShock.Config.ServerName}.\n{String.Join(", ", players.Select(player => DecoratePlayerName(player.Name)))}";
        }

        private void OnServerJoin(JoinEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Who];
            if (player == null)
                return;
            string message = $"{DecoratePlayerName(player.Name)} joined.";

            List<TSPlayer> joinedPlayers = TShock.Players.Where(joinedPlayer => joinedPlayer != null).ToList();
            List<string> joinedPlayerNames = joinedPlayers.Select(joinedPlayer => DecoratePlayerName(joinedPlayer.Name)).ToList();
            message = $"{message}\n{CurrentPlayersMessage(joinedPlayers)}";

            //Console.WriteLine("OnServerJoin: {0}", message);
            PostMessageToDiscord(message);
        }

        private void OnServerLeave(LeaveEventArgs args)
        {
            if (args.Who >= TShock.Players.Length || args.Who < 0)
            {
                //Something not right has happened
                return;
            }

            TSPlayer tsplr = TShock.Players[args.Who];
            if (tsplr == null || String.IsNullOrEmpty(tsplr.Name))
            {
                return;
            }

            string message = $"{DecoratePlayerName(tsplr.Name)} left.";

            List<TSPlayer> joinedPlayers = TShock.Players.Where(joinedPlayer => joinedPlayer != null).ToList();
            joinedPlayers.Remove(tsplr);
            message = $"{message}\n{CurrentPlayersMessage(joinedPlayers)}";

            //Console.WriteLine("OnServerLeave: {0}", message);
            PostMessageToDiscord(message);
        }


        private void OnPlayerChat(TShockAPI.Hooks.PlayerChatEventArgs args)
        {
            string message = $"{DecoratePlayerName(args.Player.Name)}: {args.RawText}";
            //Console.WriteLine("OnPlayerChat: {0}", message);
            PostMessageToDiscord(message);
        }

        private void OnServerChat(ServerChatEventArgs args)
        {
            string message = args.Text;
            //Console.WriteLine("OnServerChat: {0}", message);
            PostMessageToDiscord(message);
        }

        private void OnGetData(GetDataEventArgs e)
        {
            PacketTypes type = e.MsgID;
            TSPlayer player = TShock.Players[e.Msg.whoAmI];

            if (player != null && type == PacketTypes.PlayerDeathV2)
            {
                /*
                string message = PlayerDeathReason.FromReader(e.Msg.reader).GetDeathText(player.Name).ToString();
                if (String.IsNullOrEmpty(message))
                {
                    message = PlayerDeathReason.LegacyDefault().GetDeathText(player.Name).ToString();
                }
                */
                string message = PlayerDeathReason.LegacyDefault().GetDeathText(player.Name).ToString();
                //Console.WriteLine("OnGetData: {0}", message);
                PostMessageToDiscord(message);
            }
        }

        private void PostMessageToDiscord(string message)
        {
            //Console.WriteLine("start PostMessageToDiscord");
            if (!String.IsNullOrEmpty(Configs.DiscordWebHookUrl))
            {
                PostMessageToDiscordRunAsync(message).Wait();
            }
            //Console.WriteLine("ends PostMessageToDiscord");
        }

        private async Task PostMessageToDiscordRunAsync(string message)
        {
            string content = await PostMessageToDiscordAsync(message);
        }

        private async Task<string> PostMessageToDiscordAsync(string message)
        {
            //Console.WriteLine("start PostMessageToDiscordAsync: {0}", message);
            //return ""; // for debug
            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.PostAsync(Configs.DiscordWebHookUrl, new FormUrlEncodedContent(new Dictionary<string, string> { { "content", message } }));
            string content = await response.Content.ReadAsStringAsync();
            //Console.WriteLine("End PostMessageToDiscordAsync: {0}", content);
            return content;
        }
    }
}
