using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace InfiniteChestsV3
{
	public class PlayerInfo
	{
		public int ChestIdInUse;
		public ChestAction Action;
		public string ExtraInfo; //group, user, refilltime

		public PlayerInfo()
		{
			ChestIdInUse = -1;
			Action = ChestAction.None;
			ExtraInfo = "";
		}
	}

	public class RefillChestInfo
	{
		public int PlayerID;
		public int ChestID;
		public DateTime TimeOpened;
		public Item[] CurrentItems;
	}

	public enum ChestAction
	{
		None,
		GetInfo,
		Protect,
		TogglePublic,
		SetGroup,
		SetUser,
		SetRefill,
		Unprotect
	}
}
