# InfiniteChestsV3

This is a completely rewritten version of MarioE's original InfiniteChests plugin. Most of this plugin was copied or modified from that one.

### Plugin Features:
- Saves chests to database, allowing more than 1,000 chests per map.
- Allows conversion of chest storage to/from database at will.
- Allows chests to be "claimed" (protected) by users.
- Allows chests to be "public" (other users can edit but not destroy).
- Allows chests to "refill" (chest contents are restored at a specified interval).
- Allows players to destroy all empty chests automatically. [TODO]
- Allows players to allow only certain users and/or groups to access protected chests.
- Allows players to search the entire chest database for specific items.
- Supports usage of Key of Light/Key of Night items.

### Things this plugin will not do (as of now):
* Chest name support. Chest names are stored in tile data, which would be very costly to implement.

### Commands
```
/chest claim - Protects a chest via user account.
/chest unclaim - Unprotects a chest.
/chest info - Displays X/Y coordinates and account owner.
/chest search <item name> - Searches chests for a specific item.
/chest allow <player name> - Gives user access to chest.
/chest remove <player name> - Removes chest access from user.
/chest allowgroup <group name> - Gives players with that group access to chest.
/chest removegroup <group name> - Removes group access to chest.
/chest public - Toggles the 'public' setting of a chest, allowing others to use but not destroy the chest.
/chest refill <seconds> - Sets the interval in which chests refill items.
/chest cancel - Cancels any of the above actions.
/convchests [-r] - Converts any "real" chests to database chests (or reverse with `-r`).
/prunechests - Permanently removes empty chests from the world and database. [TODO]
/transfer - Converts the database from InfiniteChestsV2 to InfiniteChestsV3. [TODO: Add V1 conversion]
```

###Permisisons
```
ic.use - Enables use of /chest
ic.claim - Enables use of /chest claim, unclaim
ic.info - Enables use of /chest info
ic.search - Enables use of /chest search
ic.public - Enables use of /chest public
ic.protect - Players with this permission will have their chests automatically protected via user account.
ic.refill - Enables use of /chest refill
ic.edit - Allows player to edit any chest regardless of chest protection.
ic.convert - Enables use of /convchests
ic.prune - Enables use of /prunechests
ic.fix - Enables use of /fixchests
```
