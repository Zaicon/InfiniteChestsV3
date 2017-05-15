using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using TShockAPI;

namespace InfiniteChestsV3
{
	public class InfChest
	{
		public int id;
		public int userid;
		public int x;
		public int y;
		public Item[] items;
		public bool isPublic;
		public List<string> groups;
		public List<int> users;
		public int refill;
		public int worldid;
		public bool isRefill { get { return refill > -1; } }
		public bool isEmpty
		{
			get
			{
				foreach (Item item in items)
				{
					if (item.netID != 0)
						return false;
				}
				return true;
			}
		}

		public InfChest(int _userid, int _x, int _y, int _worldid)
		{
			userid = _userid;
			x = _x;
			y = _y;
			items = new Item[40];
			isPublic = false;
			groups = new List<string>();
			users = new List<int>();
			refill = -1;
			worldid = _worldid;

			for (int i = 0; i < 40; i++)
			{
				items[i] = new Item();
			}
		}

		public void StringToItems(string input)
		{
			string[] split = input.Split('~');
			List<Item> temp = new List<Item>();
			foreach (string str in split)
			{
				if (string.IsNullOrWhiteSpace(str))
					continue;

				string[] split2 = str.Split(',');
				Item item = new Item();
				item.SetDefaults(int.Parse(split2[0]));
				item.stack = int.Parse(split2[1]);
				item.prefix = byte.Parse(split2[2]);
				temp.Add(item);
			}
			items = temp.ToArray();
		}

		public string ItemsToString()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("~"); //We're adding this because then we can do SQL queries for "~itemid," for exact item matches
			foreach (Item item in items)
			{
				string temp = $"{item.netID},{item.stack},{item.prefix}~";
				sb.Append(temp);
			}
			return sb.ToString();
		}

		public void StringToUsers(string input)
		{
			string[] split = input.Split(',');

			List<int> temp = new List<int>();

			foreach (string str in split)
			{
				if (string.IsNullOrWhiteSpace(str))
					continue;
				temp.Add(int.Parse(str));
			}
			users = temp;
		}

		public string UsersToString()
		{
			return string.Join(",", users);
		}

		public void StringToGroups(string input)
		{
			string[] split = input.Split(',');

			List<string> temp = new List<string>();
			foreach (string str in split)
			{
				temp.Add(str);
			}
			groups = temp;
		}

		public string GroupsToString()
		{
			return string.Join(",", groups);
		}
	}
}
