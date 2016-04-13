///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CorradeConfiguration;
using OpenMetaverse;
using wasOpenMetaverse;
using wasSharp;
using Helpers = wasOpenMetaverse.Helpers;
using Inventory = wasOpenMetaverse.Inventory;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<CorradeCommandParameters, Dictionary<string, string>> changeappearance =
                (corradeCommandParameters, result) =>
                {
                    if (
                        !HasCorradePermission(corradeCommandParameters.Group.UUID,
                            (int) Configuration.Permissions.Grooming))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    string folder = wasInput(
                        KeyValue.Get(
                            wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.FOLDER)),
                            corradeCommandParameters.Message));
                    if (string.IsNullOrEmpty(folder))
                    {
                        throw new ScriptException(ScriptError.NO_FOLDER_SPECIFIED);
                    }
                    InventoryFolder inventoryFolder;
                    UUID folderUUID;
                    if (UUID.TryParse(folder, out folderUUID))
                    {
                        InventoryBase inventoryBaseItem =
                            Inventory.FindInventory<InventoryBase>(Client, Client.Inventory.Store.RootNode,
                                folderUUID
                                ).FirstOrDefault();
                        if (inventoryBaseItem == null)
                        {
                            throw new ScriptException(ScriptError.FOLDER_NOT_FOUND);
                        }
                        inventoryFolder = inventoryBaseItem as InventoryFolder;
                    }
                    else
                    {
                        // attempt regex and then fall back to string
                        InventoryBase inventoryBaseItem = null;
                        try
                        {
                            inventoryBaseItem =
                                Inventory.FindInventory<InventoryBase>(Client, Client.Inventory.Store.RootNode,
                                    new Regex(folder, RegexOptions.Compiled | RegexOptions.IgnoreCase))
                                    .FirstOrDefault();
                        }
                        catch (Exception)
                        {
                            // not a regex so we do not care
                            inventoryBaseItem =
                                Inventory.FindInventory<InventoryBase>(Client, Client.Inventory.Store.RootNode,
                                    folder)
                                    .FirstOrDefault();
                        }
                        if (inventoryBaseItem == null)
                        {
                            throw new ScriptException(ScriptError.FOLDER_NOT_FOUND);
                        }
                        inventoryFolder = inventoryBaseItem as InventoryFolder;
                    }
                    if (inventoryFolder == null)
                    {
                        throw new ScriptException(ScriptError.FOLDER_NOT_FOUND);
                    }
                    List<InventoryItem> equipItems = new List<InventoryItem>();
                    lock (Locks.ClientInstanceInventoryLock)
                    {
                        equipItems.AddRange(
                            Client.Inventory.Store.GetContents(inventoryFolder)
                                .AsParallel()
                                .Where(o => o is InventoryItem)
                                .Select(o => Inventory.ResolveItemLink(Client, o as InventoryItem))
                                .Where(Inventory.CanBeWorn));
                    }
                    // Check if any items are left over.
                    if (!equipItems.Any())
                    {
                        throw new ScriptException(ScriptError.NO_EQUIPABLE_ITEMS);
                    }

                    // stop non default animations if requested
                    bool deanimate;
                    switch (bool.TryParse(wasInput(
                        KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.DEANIMATE)),
                            corradeCommandParameters.Message)), out deanimate) && deanimate)
                    {
                        case true:
                            // stop all non-built-in animations
                            Client.Self.SignaledAnimations.Copy()
                                .Keys.AsParallel()
                                .Where(o => !Helpers.LindenAnimations.Contains(o))
                                .ForAll(o => { Client.Self.AnimationStop(o, true); });
                            break;
                    }

                    // Create a list of links that should be removed.
                    List<UUID> removeItems = new List<UUID>();
                    object LockObject = new object();
                    Inventory.GetCurrentOutfitFolderLinks(Client, CurrentOutfitFolder).ToArray().AsParallel().ForAll(
                        o =>
                        {
                            switch (Inventory.IsBodyPart(Client, o))
                            {
                                case true:
                                    if (
                                        equipItems.AsParallel()
                                            .Where(t => Inventory.IsBodyPart(Client, t))
                                            .Any(
                                                p =>
                                                    ((InventoryWearable) p).WearableType.Equals(
                                                        ((InventoryWearable) Inventory.ResolveItemLink(Client, o))
                                                            .WearableType)))
                                        goto default;
                                    break;
                                default:
                                    lock (LockObject)
                                    {
                                        removeItems.Add(o.UUID);
                                    }
                                    break;
                            }
                        });

                    lock (Locks.ClientInstanceInventoryLock)
                    {
                        // Now remove the links.
                        Client.Inventory.Remove(removeItems, null);

                        // Add links to new items.
                        foreach (InventoryItem item in equipItems)
                        {
                            Inventory.AddLink(Client, item, CurrentOutfitFolder);
                        }
                    }

                    // And replace the outfit wit hthe new items.
                    lock (Locks.ClientInstanceAppearanceLock)
                    {
                        Client.Appearance.ReplaceOutfit(equipItems, false);
                    }

                    // Update inventory.
                    try
                    {
                        lock (Locks.ClientInstanceInventoryLock)
                        {
                            Inventory.UpdateInventoryRecursive(Client, CurrentOutfitFolder,
                                corradeConfiguration.ServicesTimeout);
                        }
                    }
                    catch (Exception)
                    {
                        Feedback(Reflection.GetDescriptionFromEnumValue(ConsoleError.ERROR_UPDATING_INVENTORY));
                    }

                    // Schedule a rebake.
                    RebakeTimer.Change(corradeConfiguration.RebakeDelay, 0);
                };
        }
    }
}