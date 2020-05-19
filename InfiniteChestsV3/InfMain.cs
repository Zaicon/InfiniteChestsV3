using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;

namespace InfiniteChestsV3
{
	[ApiVersion(2, 1)]
	public class InfMain : TerrariaPlugin
	{
		#region Plugin Information
		public override string Author => "Zaicon";
		public override string Description => "The third version of InfiniteChests!";
		public override string Name => "InfiniteChestsV3";
		public override Version Version => Assembly.GetExecutingAssembly().GetName().Version;
		#endregion

		public InfMain(Main game) : base(game)
		{
		}



		#region Initialize/Dispose
		public override void Initialize()
		{
			ServerApi.Hooks.GameInitialize.Register(this, OnGameInitialize);
			ServerApi.Hooks.NetGetData.Register(this, OnGetData);
			ServerApi.Hooks.NetSendData.Register(this, OnSendData);
			ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);
			ServerApi.Hooks.GamePostInitialize.Register(this, OnWorldLoadAsync);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ServerApi.Hooks.GameInitialize.Deregister(this, OnGameInitialize);
				ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
				ServerApi.Hooks.NetSendData.Deregister(this, OnSendData);
				ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
				ServerApi.Hooks.GamePostInitialize.Deregister(this, OnWorldLoadAsync);
			}
			base.Dispose(disposing);
		}
		#endregion

		public static Chest chest;
		public const string PIString = "InfChestsV3.PlayerInfo";
		public static List<RefillChestInfo> RCInfos = new List<RefillChestInfo>();
		public static int nextChestId = 2;
		public static bool lockChests = false;
		public static bool usingInfChests = true;

		private void OnGameInitialize(EventArgs args)
		{
			DB.Connect();

			Commands.ChatCommands.Add(new Command("ic.use", ChestCMD, "chest") { AllowServer = false });
			Commands.ChatCommands.Add(new Command("ic.convert", ConvChestsAsync, "convchests"));
			Commands.ChatCommands.Add(new Command("ic.prune", PruneChestsAsync, "prunechests"));
			Commands.ChatCommands.Add(new Command("ic.transfer", TransferAsync, "transferchests"));
		}

		private async void OnWorldLoadAsync(EventArgs args)
		{
			await Task.Factory.StartNew(() => {
				lockChests = true;
				int count = InnerConvChests();
				TSPlayer.Server.SendInfoMessage("Converted " + count + " chests.");
				lockChests = false;
			});
		}

		private void OnGreet(GreetPlayerEventArgs args)
		{
			var player = TShock.Players[args.Who];
			if (player == null)
				return;
			PlayerInfo pinfo = new PlayerInfo()
			{
				Action = ChestAction.None,
				ChestIdInUse = -1,
				ExtraInfo = ""
			};
			player.SetData(PIString, pinfo);
		}

		private void OnGetData(GetDataEventArgs args)
		{
			if (args.Handled)
				return;

			if (!usingInfChests)
				return;

			int index = args.Msg.whoAmI;

			using (var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length)))
			{
				switch (args.MsgID)
				{
					case PacketTypes.ChestGetContents:
						{
							if (lockChests)
							{
								TShock.Players[args.Msg.whoAmI].SendWarningMessage("Chests are currently being converted. Please wait for a few moments.");
								return;
							}
							var tilex = reader.ReadInt16();
							var tiley = reader.ReadInt16();
#if DEBUG
							File.AppendAllText("debug.txt", $"[IN] 31 ChestGetContents: Tile X = {tilex} | Tile Y = {tiley}\n");
#endif
							args.Handled = true;

							#region GetChest
							InfChest gchest = DB.GetChest(tilex, tiley);
							TSPlayer gplayer = TShock.Players[index];

							if (gchest == null)
							{
								gplayer.SendErrorMessage("This chest is corrupted.");
								WorldGen.KillTile(tilex, tiley);
								TSPlayer.All.SendData(PacketTypes.Tile, "", 0, tilex, tiley + 1);
								return;
							}

							PlayerInfo info = gplayer.GetData<PlayerInfo>(PIString);

							switch (info.Action)
							{
								case ChestAction.GetInfo:
									{
										gplayer.SendInfoMessage($"X: {gchest.x} | Y: {gchest.y}");
										string owner = gchest.userid == -1 ? "(None)" : TShock.UserAccounts.GetUserAccountByID(gchest.userid) == null ? "(Deleted User)" : TShock.UserAccounts.GetUserAccountByID(gchest.userid).Name;
										string ispublic = gchest.isPublic ? " (Public)" : "";
										string isrefill = gchest.refill > -1 ? $" (Refill: {gchest.refill})" : "";
										gplayer.SendInfoMessage($"Chest Owner: {owner}{ispublic}{isrefill}");
										if (gchest.groups.Count > 0 && !string.IsNullOrWhiteSpace(gchest.groups[0]))
										{
											string tinfo = string.Join(", ", gchest.groups);
											gplayer.SendInfoMessage($"Groups Allowed: {tinfo}");
										}
										else
											gplayer.SendInfoMessage("Groups Allowed: (None)");
										if (gchest.users.Count > 0)
										{
											string tinfo = string.Join(", ", gchest.users.Select(p => TShock.UserAccounts.GetUserAccountByID(p) == null ? "(Deleted User)" : TShock.UserAccounts.GetUserAccountByID(p).Name));
											gplayer.SendInfoMessage($"Users Allowed: {tinfo}");
										}
										else
											gplayer.SendInfoMessage("Users Allowed: (None)");
										break;
									}
								case ChestAction.Protect:
									{
										if (gchest.userid == gplayer.Account.ID)
											gplayer.SendErrorMessage("This chest is already claimed by you!");
										else if (gchest.userid != -1 && !gplayer.HasPermission("ic.edit"))
											gplayer.SendErrorMessage("This chest is already claimed by someone else!");
										else
										{
											gchest.userid = gplayer.Account.ID;
											DB.UpdateUser(gchest);
											gplayer.SendSuccessMessage("This chest is now claimed by you!");
										}
										break;
									}
								case ChestAction.Unprotect:
									{
										if (gchest.userid != gplayer.Account.ID && !gplayer.HasPermission("ic.edit"))
											gplayer.SendErrorMessage("This chest is not yours!");
										else if (gchest.userid == -1)
											gplayer.SendErrorMessage("This chest is not claimed!");
										else
										{
											gchest.userid = -1;
											DB.UpdateUser(gchest);
											gplayer.SendSuccessMessage("This chest is no longer claimed.");
										}
										break;
									}
								case ChestAction.SetGroup:
									{
										if (gchest.userid != gplayer.Account.ID && !gplayer.HasPermission("ic.edit"))
											gplayer.SendErrorMessage("This chest is not yours!");
										else if (gchest.userid == -1)
											gplayer.SendErrorMessage("This chest is not claimed!");
										else
										{
											if (gchest.groups.Contains(info.ExtraInfo))
											{
												gchest.groups.Remove(info.ExtraInfo);
												gplayer.SendSuccessMessage($"Successfully removed group access from chest.");
												DB.UpdateGroups(gchest);
											}
											else
											{
												gchest.groups.Add(info.ExtraInfo);
												gplayer.SendSuccessMessage($"Successfully added group access to chest.");
												DB.UpdateGroups(gchest);
											}
										}
										break;
									}
								case ChestAction.SetRefill:
									{
										if (gchest.userid != gplayer.Account.ID && !gplayer.HasPermission("ic.edit"))
											gplayer.SendErrorMessage("This chest is not yours!");
										else if (gchest.userid == -1)
											gplayer.SendErrorMessage("This chest is not claimed!");
										else
										{
											int refilltime = int.Parse(info.ExtraInfo);
											gchest.refill = refilltime;
											DB.UpdateRefill(gchest);
											gplayer.SendSuccessMessage("Successfull set refill time to " + (refilltime == -1 ? "(none)." : refilltime.ToString() + "."));
										}
										break;
									}
								case ChestAction.SetUser:
									{
										if (gchest.userid != gplayer.Account.ID && !gplayer.HasPermission("ic.edit"))
											gplayer.SendErrorMessage("This chest is not yours!");
										else if (gchest.userid == -1)
											gplayer.SendErrorMessage("This chest is not claimed!");
										else
										{
											int userid = int.Parse(info.ExtraInfo);
											if (gchest.users.Contains(userid))
											{
												gchest.users.Remove(userid);
												DB.UpdateUsers(gchest);
												gplayer.SendSuccessMessage("Successfully removed user access from chest.");
											}
											else
											{
												gchest.users.Add(userid);
												DB.UpdateUsers(gchest);
												gplayer.SendSuccessMessage("Successfully added user access to chest.");
											}
										}
										break;
									}
								case ChestAction.TogglePublic:
									{
										if (gchest.userid != gplayer.Account.ID && !gplayer.HasPermission("ic.edit"))
											gplayer.SendErrorMessage("This chest is not yours!");
										else if (gchest.userid == -1)
											gplayer.SendErrorMessage("This chest is not claimed!");
										else
										{
											if (gchest.isPublic)
											{
												gchest.isPublic = false;
												DB.UpdatePublic(gchest);
												gplayer.SendSuccessMessage("Successfully set chest as private.");
											}
											else
											{
												gchest.isPublic = true;
												DB.UpdatePublic(gchest);
												gplayer.SendSuccessMessage("Successfully set chest as public.");
											}
										}
										break;
									}
								case ChestAction.None:
									{
										//check for perms
										if (gchest.userid != -1 && !gchest.isPublic && !gchest.groups.Contains(gplayer.Group.Name) && !gplayer.HasPermission("ic.edit") && gplayer.IsLoggedIn && gchest.userid != gplayer.Account.ID && !gchest.users.Contains(gplayer.Account.ID))
										{
											gplayer.SendErrorMessage("This chest is protected.");
											break;
										}

										info.ChestIdInUse = gchest.id;

										Item[] items;
										RefillChestInfo rcinfo = GetRCInfo(!gplayer.IsLoggedIn ? -1 : gplayer.Account.ID, gchest.id);

										//use refill items if exists, or create new refill entry, or use items directly
										if (gchest.isRefill && rcinfo != null && (DateTime.Now - rcinfo.TimeOpened).TotalSeconds < gchest.refill)
											items = rcinfo.CurrentItems;
										else if (gchest.isRefill)
										{
											if (rcinfo != null)
												DeleteOldRCInfo(!gplayer.IsLoggedIn ? -1 : gplayer.Account.ID, gchest.id);

											RefillChestInfo newrcinfo = new RefillChestInfo()
											{
												ChestID = gchest.id,
												CurrentItems = gchest.items,
												PlayerID = !gplayer.IsLoggedIn ? -1 : gplayer.Account.ID,
												TimeOpened = DateTime.Now
											};
											RCInfos.Add(newrcinfo);
											items = newrcinfo.CurrentItems;
										}
										else
											items = gchest.items;

										int tempchest = GetNextChestId();
										Main.chest[tempchest] = new Chest()
										{
											item = items,
											x = gchest.x,
											y = gchest.y
										};

										for (int i = 0; i < 40; i++)
										{
											gplayer.SendData(PacketTypes.ChestItem, "", tempchest, i, gchest.items[i].stack, gchest.items[i].prefix, gchest.items[i].netID);
										}
										gplayer.SendData(PacketTypes.ChestOpen, "", tempchest, gchest.x, gchest.y);
										NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, index, NetworkText.Empty, index, tempchest);

										Main.chest[tempchest] = null;
										break;
									}
							}
							info.Action = ChestAction.None;
							info.ExtraInfo = "";
							gplayer.SetData(PIString, info);
							#endregion

							break;
						}
					case PacketTypes.ChestItem:
						{
							if (lockChests)
							{
								TShock.Players[args.Msg.whoAmI].SendWarningMessage("Chests are currently being converted. Please wait for a few moments.");
								return;
							}

							var chestid = reader.ReadInt16();
							var itemslot = reader.ReadByte();
							var stack = reader.ReadInt16();
							var prefix = reader.ReadByte();
							var netid = reader.ReadInt16();
#if DEBUG
							if (itemslot == 0 || itemslot == 39)
								File.AppendAllText("debug.txt", $"[IN] 32 ChestItem: Chest ID = {chestid} | Item Slot = {itemslot} | Stack = {stack} | Prefix = {prefix} | Net ID = {netid}\n");
#endif

							TSPlayer ciplayer = TShock.Players[index];
							PlayerInfo piinfo = ciplayer.GetData<PlayerInfo>(PIString);
							if (piinfo.ChestIdInUse == -1)
								return;
							InfChest cichest = DB.GetChest(piinfo.ChestIdInUse);
							RefillChestInfo circinfo = GetRCInfo(!TShock.Players[index].IsLoggedIn ? -1 : TShock.Players[index].Account.ID, cichest.id);
							if (cichest == null)
							{
								ciplayer.SendWarningMessage("This chest is corrupted. Please remove it.");
								return;
							}

							Item item = new Item();
							item.SetDefaults(netid);
							item.stack = stack;
							item.prefix = prefix;

							if (ciplayer.HasPermission(Permissions.spawnmob) && Main.hardMode && (item.netID == 3091 && (ciplayer.TPlayer.ZoneCrimson || ciplayer.TPlayer.ZoneCorrupt)))
							{
								bool empty = true;
								foreach (var initem in cichest.items)
								{
									if (initem?.netID != 0)
									{
										empty = false;
									}
								}
								if (empty)
								{
									//kick player out of chest, kill chest, spawn appropriate mimic
									piinfo.ChestIdInUse = -1;
									ciplayer.SetData(PIString, piinfo);
									NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, index, NetworkText.Empty, index, -1);
									DB.DeleteChest(cichest.id);
									WorldGen.KillTile(cichest.x, cichest.y, noItem: true);
									NetMessage.SendTileSquare(ciplayer.Index, cichest.x, cichest.y, 3);

									int type;
									if (netid == 3092)
										type = 475;
									else if (netid == 3091 && ciplayer.TPlayer.ZoneCrimson)
										type = 474;
									else //if (netid == 3091 && ciplayer.TPlayer.ZoneCorrupt)
										type = 473;

									var npc = TShock.Utils.GetNPCById(type);
									TSPlayer.Server.SpawnNPC(npc.type, npc.FullName, 1, ciplayer.TileX, ciplayer.TileY, 10, 10);
								}
							}

							if (cichest.isRefill)
								circinfo.CurrentItems[itemslot] = item;
							else
							{
								cichest.items[itemslot] = item;
								DB.UpdateItems(cichest);
							}

							break;
						}
					case PacketTypes.ChestOpen:
						{
							var chestid = reader.ReadInt16();
							var chestx = reader.ReadInt16();
							var chesty = reader.ReadInt16();
							var namelength = reader.ReadByte();
							string chestname = null;
							if (namelength > 0)
							{
								chestname = reader.ReadString();
								return;
							}
#if DEBUG
							File.AppendAllText("debug.txt", $"[IN] 33 ChestName: Chest ID = {chestid} | Chest X = {chestx} | Chest Y = {chesty} | Name Length = {namelength} | Chest Name = {chestname}\n");
#endif

							if (chestid == -1)
							{
								PlayerInfo coinfo = TShock.Players[index].GetData<PlayerInfo>(PIString);
								coinfo.ChestIdInUse = -1;
								TShock.Players[index].SetData(PIString, coinfo);
								NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, index, NetworkText.Empty, index, -1);
							}

							break;
						}
					case PacketTypes.PlaceChest:
						{
							if (lockChests)
							{
								TShock.Players[args.Msg.whoAmI].SendWarningMessage("Chests are currently being converted. Please wait for a few moments.");
								return;
							}

							args.Handled = true;

							var action = reader.ReadByte(); //0 placec 1 killc 2 placed 3 killd 4 placegc
							var tilex = reader.ReadInt16();
							var tiley = reader.ReadInt16();
							var style = reader.ReadInt16();
							// Ignoring "chest ID to destroy" because we aren't using chest IDs
							reader.ReadInt16(); 
							//21 chest
							//88 dresser
							//467 golden/crystal chest
							int chesttype;
							if (action == 0 || action == 1)
								chesttype = 21;
							else if (action == 2 || action == 3)
								chesttype = 88;
							else if (action == 4 || action == 5)
								chesttype = 467;
							else
								throw new Exception();

							if (action == 0 || action == 2 || action == 4)
							{
								if (TShock.Regions.CanBuild(tilex, tiley, TShock.Players[index]))
								{
									Task.Factory.StartNew(() =>
									{
										int temp_tile_x = tilex;
										if (action == 2)
											temp_tile_x--;
										InfChest newChest = new InfChest(TShock.Players[index].HasPermission("ic.protect") ? TShock.Players[index].Account.ID : -1, temp_tile_x, tiley - 1, Main.worldID);
										DB.AddChest(newChest);
									});

									int success = WorldGen.PlaceChest(tilex, tiley, (ushort)chesttype, false, style);
									if (success == -1)
									{
										NetMessage.TrySendData((int)PacketTypes.PlaceChest, index, -1, null, action, tilex, tiley, style);
										Item.NewItem(tilex * 16, tiley * 16, 32, 32, Chest.chestItemSpawn[style], 1, noBroadcast: true);
									}
									else
									{
										Main.chest[0] = null;
										NetMessage.SendData((int)PacketTypes.PlaceChest, -1, -1, null, action, tilex, tiley, style);
									}
								}
							}
							else
							{
								if (Main.tile[tilex, tiley].type != 21 && Main.tile[tilex, tiley].type != 88 && Main.tile[tilex, tiley].type != 467)
									return;
								if (TShock.Regions.CanBuild(tilex, tiley, TShock.Players[index]))
								{
									if (Main.tile[tilex, tiley].frameY % 36 != 0)
										tiley--;
									if ((action == 1 || action == 5) && Main.tile[tilex, tiley].frameX % 36 != 0)
										tilex--;
									if (action == 3)
										tilex -= (short)(Main.tile[tilex, tiley].frameX % 54 / 18);
									#region Kill Chest
									Task.Factory.StartNew(() =>
									{
										InfChest chest = DB.GetChest(tilex, tiley);
										TSPlayer player = TShock.Players[index];

										//If chest exists in map but not db, something went wrong
										if (chest == null)
										{
											player.SendWarningMessage("This chest is corrupted.");
											WorldGen.KillTile(tilex, tiley);
											TSPlayer.All.SendData(PacketTypes.Tile, null, 0, tilex, tiley + 1);
										}
										//check for perms - chest owner, claim, edit perm
										else if (chest.userid != player.Account.ID && chest.userid != -1 && !player.HasPermission("ic.edit"))
										{
											player.SendErrorMessage("This chest is protected.");
											player.SendTileSquare(tilex, tiley, 3);
										}
										//check for empty chest
										else if (!chest.isEmpty)
										{
											player.SendTileSquare(tilex, tiley, 3);
										}
										else
										{
											int tempchest = GetNextChestId();
											Main.chest[tempchest] = new Chest()
											{
												item = new Item[40],
												x = tilex,
												y = tiley
											};

											WorldGen.KillTile(tilex, tiley);
											DB.DeleteChest(chest.id);
											NetMessage.TrySendData((int)PacketTypes.PlaceChest, -1, -1, null, tempchest, tilex, tiley);
										}
									});
									#endregion
								}
							}

#if DEBUG
							File.AppendAllText("debug.txt", $"[IN] 34 PlaceChest: Action = {action} | Tile X = {tilex} | Tile Y = {tiley} | Style = {style}\n");
#endif
							break;
						}
					case PacketTypes.ChestName:
						{
							var chestid = reader.ReadInt16();
							var chestx = reader.ReadInt16();
							var chesty = reader.ReadInt16();
#if DEBUG
							File.AppendAllText("debug.txt", $"[IN] 69 GetChestName: Chest ID = {chestid} | Chest X = {chestx} | Chest Y = {chesty}\n");
#endif
							break;
						}
				}
			}

		}

		private void OnSendData(SendDataEventArgs args)
		{
			switch (args.MsgId)
			{
				case PacketTypes.ChestItem:
#if DEBUG
					File.AppendAllText("debug.txt", $"[OUT] 32 ChestItem: Remote = {args.remoteClient} | Ignore = {args.ignoreClient} | Chest ID = {args.number} | Item Slot = {args.number2} | Stack = {args.number3} | Prefix = {args.number4} | Net ID = {args.number5}\n");
#endif
					break;
				case PacketTypes.ChestOpen:
#if DEBUG
					File.AppendAllText("debug.txt", $"[OUT] 33 SetChestName: Remote = {args.remoteClient} | Ignore = {args.ignoreClient} | Chest ID = {args.number} | Chest X = {args.number2} | Chest Y = {args.number3} | Name Length = {args.number4} | Chest Name = {args.text}\n");
#endif
					break;
				case PacketTypes.PlaceChest:
#if DEBUG
					File.AppendAllText("debug.txt", $"[OUT] 34 PlaceChest: Remote = {args.remoteClient} | Ignore = {args.ignoreClient} | Action = {args.number} | Tile X = {args.number2} | Tile Y = {args.number3} | Style = {args.number4} | Chest ID = {args.number5}\n");
#endif
					break;
				case PacketTypes.ChestName:
#if DEBUG
					File.AppendAllText("debug.txt", $"[OUT] 69 GetChestName: Remote = {args.remoteClient} | Ignore = {args.ignoreClient} | Chest ID = {args.number} | Chest X = {args.number2} | Chest Y = {args.number3} | Chest Name = {args.text}\n");
#endif
					break;
				case PacketTypes.SyncPlayerChestIndex:
#if DEBUG
					File.AppendAllText("debug.txt", $"[OUT] 80 SyncPlayerChestIndex: Remote = {args.remoteClient} | Ignore = {args.ignoreClient} | Player = {args.number} | Chest = {args.number2}\n");
#endif
					break;
			}
		}

		private void ChestCMD(CommandArgs args)
		{
			if (!usingInfChests)
			{
				args.Player.SendErrorMessage("InfiniteChests are currently disabled on this server.");
				return;
			}

			if (args.Parameters.Count == 0 || args.Parameters[0].ToLower() == "help")
			{
				List<string> help = new List<string>();

				args.Player.SendErrorMessage("Invalid syntax:");
				if (args.Player.HasPermission("ic.claim"))
					help.Add("/chest <claim/unclaim>");
				if (args.Player.HasPermission("ic.info"))
					help.Add("/chest info");
				if (args.Player.HasPermission("ic.search"))
					help.Add("/chest search <item name>");
				if (args.Player.HasPermission("ic.claim"))
				{
					help.Add("/chest allow <player name>");
					help.Add("/chest remove <player name>");
					help.Add("/chest allowgroup <group name>");
					help.Add("/chest removegroup <group name>");
				}
				if (args.Player.HasPermission("ic.public"))
					help.Add("/chest public");
				if (args.Player.HasPermission("ic.refill"))
					help.Add("/chest refill <seconds>");
				help.Add("/chest cancel");

				int pageNumber;
				if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
					return;

				PaginationTools.SendPage(args.Player, pageNumber, help, new PaginationTools.Settings() { HeaderFormat = "Chest Subcommands ({0}/{1}):", FooterFormat = "Type /chest help {0} for more." });

				return;
			}

			PlayerInfo info = args.Player.GetData<PlayerInfo>(PIString);

			switch (args.Parameters[0].ToLower())
			{
				case "claim":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to claim chests.");
						break;
					}
					args.Player.SendInfoMessage("Open a chest to claim it.");
					info.Action = ChestAction.Protect;
					break;
				case "unclaim":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to claim chests.");
						break;
					}
					args.Player.SendInfoMessage("Open a chest to unclaim it.");
					info.Action = ChestAction.Unprotect;
					break;
				case "info":
					if (!args.Player.HasPermission("ic.info"))
					{
						args.Player.SendErrorMessage("You do not have permission to view chest info.");
						break;
					}
					args.Player.SendInfoMessage("Open a chest to get information about it.");
					info.Action = ChestAction.GetInfo;
					break;
				case "search":
					if (!args.Player.HasPermission("ic.search"))
					{
						args.Player.SendErrorMessage("You do not have permission to search for chest items.");
						break;
					}
					if (args.Parameters.Count < 2)
					{
						args.Player.SendErrorMessage("Invalid syntax: /chest search <item name>");
						break;
					}
					string name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));

					Item exactmatch = null;
					List<Item> partialMatches = new List<Item>();
					for (int i = 0; i < Main.maxItemTypes; i++)
					{
						Item item = new Item();
						item.SetDefaults(i);

						if (item.Name.ToLower() == name.ToLower())
						{
							exactmatch = item;
							break;
						}
						else if (item.Name.ToLower().Contains(name.ToLower()))
						{
							partialMatches.Add(item);
						}
					}
					if (exactmatch != null)
					{
						int count = DB.SearchChests(exactmatch.netID);
						args.Player.SendSuccessMessage($"There are {count} chest(s) with {exactmatch.Name}(s).");
					}
					else if (partialMatches.Count == 1)
					{
						int count = DB.SearchChests(partialMatches[0].netID);
						args.Player.SendSuccessMessage($"There are {count} chest(s) with {partialMatches[0].Name}(s).");
					}
					else if (partialMatches.Count > 1)
						args.Player.SendErrorMessage($"Multiple matches found for item '{name}'.");
					else
						args.Player.SendErrorMessage($"No matches found for item '{name}'.");
					break;
				case "allow":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to allow other users to access this chest.");
						return;
					}
					if (args.Parameters.Count < 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest allow <player name>");
					else
					{
						name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
						var user = TShock.UserAccounts.GetUserAccountByName(name);

						if (user == null)
						{
							args.Player.SendErrorMessage("No player found by the name " + name);
							return;
						}
						info.ExtraInfo = user.ID.ToString();
						info.Action = ChestAction.SetUser;
						args.Player.SendInfoMessage("Open a chest to allow " + name + " to access it.");
					}
					break;
				case "remove":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to remove chest access from other users.");
						return;
					}
					if (args.Parameters.Count < 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest remove <player name>");
					else
					{
						name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
						var user = TShock.UserAccounts.GetUserAccountByName(name);

						if (user == null)
						{
							args.Player.SendErrorMessage("No player found by the name " + name);
							return;
						}
						info.ExtraInfo = user.ID.ToString();
						info.Action = ChestAction.SetUser;
						args.Player.SendInfoMessage("Open a chest to remove chest access from  " + name + ".");
					}
					break;
				case "allowgroup":
				case "allowg":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to allow other groups to access this chest.");
						return;
					}
					if (args.Parameters.Count != 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest allowgroup <group name>");
					else
					{
						var group = TShock.Groups.GetGroupByName(args.Parameters[1]);

						if (group == null)
						{
							args.Player.SendErrorMessage("No group found by the name " + args.Parameters[1]);
							return;
						}
						info.ExtraInfo = group.Name;
						info.Action = ChestAction.SetGroup;
						args.Player.SendInfoMessage("Open a chest to allow users from the group " + group.Name + " to access it.");
					}
					break;
				case "removegroup":
				case "removeg":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to remove chest access from other groups.");
						return;
					}
					if (args.Parameters.Count != 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest removegroup <group name>");
					else
					{
						var group = TShock.Groups.GetGroupByName(args.Parameters[1]);

						if (group == null)
						{
							args.Player.SendErrorMessage("No group found by the name " + args.Parameters[1]);
							return;
						}
						info.ExtraInfo = group.Name;
						info.Action = ChestAction.SetGroup;
						args.Player.SendInfoMessage("Open a chest to remove chest access from users in the group " + group.Name + ".");
					}
					break;
				case "public":
					if (!args.Player.HasPermission("ic.public"))
					{
						args.Player.SendErrorMessage("You do not have permission to change a chest's public setting.");
						break;
					}
					info.Action = ChestAction.TogglePublic;
					args.Player.SendInfoMessage("Open a chest to toggle the chest's public setting.");
					break;
				case "refill":
					if (!args.Player.HasPermission("ic.refill"))
					{
						args.Player.SendErrorMessage("You do not have permission to set a chest's refill time.");
						break;
					}
					if (args.Parameters.Count != 2) // /chest refill <time>
					{
						args.Player.SendErrorMessage("Invalid syntax: /chest refill <seconds>");
						break;
					}
					int refillTime;
					if (!int.TryParse(args.Parameters[1], out refillTime) || refillTime < -1 || refillTime > 99999)
					{
						args.Player.SendErrorMessage("Invalid refill time.");
						break;
					}
					info.Action = ChestAction.SetRefill;
					info.ExtraInfo = refillTime.ToString();
					if (refillTime != -1)
						args.Player.SendInfoMessage("Open a chest to set its refill time to " + refillTime + " seconds.");
					else
						args.Player.SendInfoMessage("Open a chest to remove auto-refill.");
					break;
				case "cancel":
					info.Action = ChestAction.None;
					args.Player.SendInfoMessage("Canceled chest action.");
					break;
				default:
					args.Player.SendErrorMessage("Invalid syntax. Use '/chest help' for help.");
					break;
			}
		}

		private async void ConvChestsAsync(CommandArgs args)
		{
			if (args.Parameters.Count > 0 && args.Parameters[0] == "-r")
			{
				if (!usingInfChests)
				{
					args.Player.SendErrorMessage("This command has already been run!");
					return;
				}
				if (DB.GetCount() > 1000)
				{
					args.Player.SendErrorMessage("There are more than 1000 chests in the database, which is more than the map can hold.");
					return;
				}
				args.Player.SendInfoMessage("Restoring chests. Please wait...");
				await Task.Factory.StartNew(() =>
				{
					lockChests = true;
					var chestlist = DB.GetAllChests();
					for (int i = 0; i < chestlist.Count; i++)
					{
						Main.chest[i] = chestlist[i];
					}
					DB.DeleteAllChests();
					usingInfChests = false;
					lockChests = false;
					args.Player.SendSuccessMessage("Chests restored and InfiniteChests has been disabled until server restart or /convchests is used again.");
					TShock.Utils.SaveWorld();
				});
				return;
			}

			await Task.Factory.StartNew(() =>
			 {
				 args.Player.SendInfoMessage("Converting chests. Please wait...");
				 lockChests = true;
				//Kick all players out of chests
				foreach (var player in TShock.Players)
				 {
					 if (player == null)
						 continue;

					 var info = player.GetData<PlayerInfo>(PIString);
					 if (info.ChestIdInUse != -1)
					 {
						 NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, -1, NetworkText.Empty, player.Index, -1);
					 }
					 info.ChestIdInUse = -1;
					 player.SetData(PIString, info);
				 }
				 int count = InnerConvChests();
				 lockChests = false;
				 args.Player.SendSuccessMessage("Converted " + count + " chests.");
				 usingInfChests = true;
			 });
		}

		private async void PruneChestsAsync(CommandArgs args)
		{
			args.Player.SendInfoMessage("Pruning all empty chests. Please wait...");
			await Task.Factory.StartNew(() =>
			{
				var points = DB.DeleteEmptyChests();
				foreach (Point point in points)
				{
					WorldGen.KillTile(point.X, point.Y, noItem: true);
					NetMessage.SendTileSquare(args.Player.Index, point.X, point.Y, 3);
				}
				args.Player.SendSuccessMessage("Pruned " + points.Count + " chests.");
			});
		}

		private RefillChestInfo GetRCInfo(int playerID, int chestID)
		{
			return RCInfos.FirstOrDefault(e => e.PlayerID == playerID && e.ChestID == chestID);
		}

		private void DeleteOldRCInfo(int playerID, int chestID)
		{
			RCInfos.RemoveAll(e => e.PlayerID == playerID && e.ChestID == chestID);
		}

		private int GetNextChestId()
		{
			nextChestId++;
			if (nextChestId == 998)
				nextChestId = 2;
			return nextChestId;
		}

		private int InnerConvChests()
		{
			int count = 0;
			for (int i = 0; i < Main.chest.Length; i++)
			{
				if (Main.chest[i] != null)
				{
					InfChest chest = new InfChest(-1, Main.chest[i].x, Main.chest[i].y, Main.worldID)
					{
						groups = new List<string>(),
						isPublic = false,
						items = Main.chest[i].item,
						refill = -1,
						users = new List<int>()
					};
					DB.AddChest(chest);
					Main.chest[i] = null;
					count++;
				}
			}
			TShock.Utils.SaveWorld();
			return count;
		}

		private async void TransferAsync(CommandArgs args)
		{
			if (args.Parameters.Count != 1)
			{
				args.Player.SendErrorMessage("Please specify whether you want to transfer version 1 ('v1') or version 2 ('v2') chest database!");
				return;
			}
			switch (args.Parameters[0].ToLower())
			{
				case "version 1":
				case "version1":
				case "v1":
				case "1":
					args.Player.SendInfoMessage("Converting chests from previous database. This may take a few minutes.");
					await Task.Factory.StartNew(() =>
					{
						int count = DB.TransferV1();
						args.Player.SendSuccessMessage("Transfer complete. Count: " + count);
					});
					break;
				case "version 2":
				case "version2":
				case "v2":
				case "2":
					args.Player.SendInfoMessage("Converting chests from previous database. This may take a few minutes.");
					await Task.Factory.StartNew(() =>
					{
						int count = DB.TransferV2();
						args.Player.SendSuccessMessage("Transfer complete. Count: " + count);
					});
					break;
				default:
					args.Player.SendErrorMessage("Invalid option. Use /transferchests v1 or v2 to choose a database conversion type.");
					break;
			}
		}
	}
}
