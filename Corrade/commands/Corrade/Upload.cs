///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using Corrade.Constants;
using CorradeConfigurationSharp;
using ImageMagick;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using wasOpenMetaverse;
using wasSharp;
using Graphics = wasOpenMetaverse.Graphics;
using Inventory = wasOpenMetaverse.Inventory;
using Reflection = wasSharp.Reflection;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static readonly Action<Command.CorradeCommandParameters, Dictionary<string, string>> upload =
                (corradeCommandParameters, result) =>
                {
                    if (
                        !HasCorradePermission(corradeCommandParameters.Group.UUID,
                            (int)Configuration.Permissions.Inventory))
                    {
                        throw new Command.ScriptException(Enumerations.ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    var name =
                        wasInput(KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.NAME)),
                            corradeCommandParameters.Message));
                    if (string.IsNullOrEmpty(name))
                    {
                        throw new Command.ScriptException(Enumerations.ScriptError.NO_NAME_PROVIDED);
                    }
                    var permissions = PermissionMask.None;
                    CSV.ToEnumerable(
                        wasInput(
                            KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.PERMISSIONS)),
                                corradeCommandParameters.Message)))
                        .AsParallel()
                        .Where(o => !string.IsNullOrEmpty(o))
                        .ForAll(
                            o => typeof(PermissionMask).GetFields(BindingFlags.Public | BindingFlags.Static)
                                .AsParallel()
                                .Where(p => string.Equals(o, p.Name, StringComparison.Ordinal))
                                .ForAll(
                                    q =>
                                    {
                                        BitTwiddling.SetMaskFlag(ref permissions, (PermissionMask)q.GetValue(null));
                                    }));
                    var assetTypeInfo = typeof(AssetType).GetFields(BindingFlags.Public |
                                                                    BindingFlags.Static)
                        .AsParallel().FirstOrDefault(o =>
                            o.Name.Equals(
                                wasInput(
                                    KeyValue.Get(
                                        Reflection.GetNameFromEnumValue(
                                            Command.ScriptKeys.TYPE),
                                        corradeCommandParameters.Message)),
                                StringComparison.Ordinal));
                    if (assetTypeInfo == null)
                    {
                        throw new Command.ScriptException(Enumerations.ScriptError.UNKNOWN_ASSET_TYPE);
                    }
                    var assetType = (AssetType)assetTypeInfo.GetValue(null);
                    byte[] data;
                    try
                    {
                        data = Convert.FromBase64String(
                            wasInput(
                                KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DATA)),
                                    corradeCommandParameters.Message)));
                    }
                    catch (Exception)
                    {
                        throw new Command.ScriptException(Enumerations.ScriptError.INVALID_ASSET_DATA);
                    }

                    var csv = new List<string>();
                    var succeeded = false;

                    // If an item was specified then update instead of creating a new item for certain asset types.
                    var item =
                        wasInput(
                            KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.ITEM)),
                                corradeCommandParameters.Message));
                    InventoryItem inventoryItem = null;
                    if (!string.IsNullOrEmpty(item))
                    {
                        var itemUUID = UUID.Zero;
                        switch (UUID.TryParse(item, out itemUUID))
                        {
                            case true:
                                Locks.ClientInstanceInventoryLock.EnterReadLock();
                                if (Client.Inventory.Store.Contains(itemUUID))
                                {
                                    inventoryItem = Client.Inventory.Store[itemUUID] as InventoryItem;
                                }
                                Locks.ClientInstanceInventoryLock.ExitReadLock();
                                break;

                            default:
                                inventoryItem = Inventory.FindInventory<InventoryBase>(Client, item,
                                    CORRADE_CONSTANTS.PATH_SEPARATOR, CORRADE_CONSTANTS.PATH_SEPARATOR_ESCAPE,
                                    corradeConfiguration.ServicesTimeout) as InventoryItem;
                                break;
                        }

                        if (inventoryItem == null || !inventoryItem.AssetType.Equals(assetType))
                            throw new Command.ScriptException(Enumerations.ScriptError.INVENTORY_ITEM_NOT_FOUND);
                    }

                    switch (assetType)
                    {
                        case AssetType.Texture:
                        case AssetType.Animation:
                            // the holy asset trinity is charged money
                            if (
                                !HasCorradePermission(corradeCommandParameters.Group.UUID,
                                    (int)Configuration.Permissions.Economy))
                            {
                                throw new Command.ScriptException(Enumerations.ScriptError.NO_CORRADE_PERMISSIONS);
                            }
                            if (!Services.UpdateBalance(Client, corradeConfiguration.ServicesTimeout))
                            {
                                throw new Command.ScriptException(
                                    Enumerations.ScriptError.UNABLE_TO_OBTAIN_MONEY_BALANCE);
                            }
                            Locks.ClientInstanceSelfLock.EnterReadLock();
                            if (Client.Self.Balance < Client.Settings.UPLOAD_COST)
                            {
                                Locks.ClientInstanceSelfLock.ExitReadLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.INSUFFICIENT_FUNDS);
                            }
                            Locks.ClientInstanceSelfLock.ExitReadLock();
                            switch (assetType)
                            {
                                case AssetType.Texture:
                                    ManagedImage managedImage;
                                    // If the user did not send a JPEG-2000 Codestream, attempt to convert the data
                                    // and then encode to JPEG-2000 Codestream since that is what Second Life expects.
                                    if (!OpenJPEG.DecodeToImage(data, out managedImage))
                                    {
                                        /*
                                         * Use ImageMagick on Windows and the .NET converter otherwise.
                                         */
                                        switch (Utils.GetRunningPlatform())
                                        {
                                            case Utils.Platform.Windows:
                                                try
                                                {
                                                    using (var magickImage = new MagickImage(data))
                                                    {
                                                        using (var image = new Bitmap(magickImage.ToBitmap()))
                                                        {
                                                            var size = Graphics.GetScaleTextureSize(
                                                                image.Width, image.Height);

                                                            using (
                                                                var newImage = new Bitmap(size.Width, size.Height,
                                                                    PixelFormat.Format24bppRgb))
                                                            {
                                                                using (
                                                                    var g =
                                                                        System.Drawing.Graphics.FromImage(newImage))
                                                                {
                                                                    g.SmoothingMode =
                                                                        SmoothingMode
                                                                            .HighQuality;
                                                                    g.InterpolationMode =
                                                                        InterpolationMode
                                                                            .HighQualityBicubic;
                                                                    g.DrawImage(image, 0, 0, size.Width, size.Height);
                                                                    data = OpenJPEG.EncodeFromImage(newImage, true);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception)
                                                {
                                                    throw new Command.ScriptException(
                                                        Enumerations.ScriptError.UNKNOWN_IMAGE_FORMAT_PROVIDED);
                                                }

                                                break;

                                            default:
                                                try
                                                {
                                                    using (var imageByteStream = new MemoryStream(data))
                                                    {
                                                        using (var image = Image.FromStream(imageByteStream))
                                                        {
                                                            var size = Graphics.GetScaleTextureSize(
                                                                image.Width, image.Height);

                                                            using (
                                                                var newImage = new Bitmap(size.Width, size.Height,
                                                                    PixelFormat.Format24bppRgb))
                                                            {
                                                                using (
                                                                    var g = System.Drawing.Graphics.FromImage(newImage))
                                                                {
                                                                    g.SmoothingMode =
                                                                        SmoothingMode
                                                                            .HighQuality;
                                                                    g.InterpolationMode =
                                                                        InterpolationMode
                                                                            .HighQualityBicubic;
                                                                    g.DrawImage(image, 0, 0, size.Width, size.Height);
                                                                    data = OpenJPEG.EncodeFromImage(newImage, true);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception)
                                                {
                                                    throw new Command.ScriptException(
                                                        Enumerations.ScriptError.UNKNOWN_IMAGE_FORMAT_PROVIDED);
                                                }
                                                break;
                                        }
                                    }
                                    break;
                            }
                            // ...now create and upload the asset
                            var CreateItemFromAssetEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestCreateItemFromAsset(data, name,
                                    wasInput(
                                        KeyValue.Get(
                                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                            corradeCommandParameters.Message)),
                                    assetType,
                                    (InventoryType)
                                        typeof(InventoryType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                            .AsParallel().FirstOrDefault(
                                                o => o.Name.Equals(Enum.GetName(typeof(AssetType), assetType),
                                                    StringComparison.Ordinal)).GetValue(null),
                                    Client.Inventory.FindFolderForType(assetType),
                                    delegate (bool completed, string status, UUID itemID, UUID assetID)
                                    {
                                        inventoryItem = Client.Inventory.Store[itemID] as InventoryItem;
                                        succeeded = completed;
                                        CreateItemFromAssetEvent.Set();
                                    });
                            if (!CreateItemFromAssetEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_UPLOADING_ASSET);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        case AssetType.SoundWAV:
                        case AssetType.Sound:
                            UUID soundUUID;
                            Locks.ClientInstanceAssetsLock.EnterWriteLock();
                            soundUUID = Client.Assets.RequestUpload(assetType, data, false);
                            Locks.ClientInstanceAssetsLock.ExitWriteLock();
                            if (soundUUID.Equals(UUID.Zero))
                            {
                                throw new Command.ScriptException(Enumerations.ScriptError.ASSET_UPLOAD_FAILED);
                            }
                            var CreateSoundEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestCreateItemFromAsset(data, name,
                                    wasInput(
                                        KeyValue.Get(
                                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                            corradeCommandParameters.Message)),
                                    assetType,
                                    (InventoryType)
                                        typeof(InventoryType).GetFields(BindingFlags.Public | BindingFlags.Static)
                                            .AsParallel().FirstOrDefault(
                                                o => o.Name.Equals(Enum.GetName(typeof(AssetType), assetType),
                                                    StringComparison.Ordinal)).GetValue(null),
                                    Client.Inventory.FindFolderForType(assetType),
                                    delegate (bool completed, string status, UUID itemID, UUID assetID)
                                    {
                                        inventoryItem = Client.Inventory.Store[itemID] as InventoryItem;
                                        succeeded = completed;
                                        CreateSoundEvent.Set();
                                    });
                            if (!CreateSoundEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        case AssetType.Bodypart:
                        case AssetType.Clothing:
                            var wearTypeInfo = typeof(MuteType).GetFields(BindingFlags.Public |
                                                                          BindingFlags.Static)
                                .AsParallel().FirstOrDefault(
                                    o =>
                                        o.Name.Equals(
                                            wasInput(
                                                KeyValue.Get(
                                                    wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.WEAR)),
                                                    corradeCommandParameters.Message)),
                                            StringComparison.Ordinal));
                            if (wearTypeInfo == null)
                            {
                                throw new Command.ScriptException(Enumerations.ScriptError.UNKNOWN_WEARABLE_TYPE);
                            }
                            Locks.ClientInstanceAssetsLock.EnterWriteLock();
                            var wearableUUID = Client.Assets.RequestUpload(assetType, data, false);
                            Locks.ClientInstanceAssetsLock.ExitWriteLock();
                            if (wearableUUID.Equals(UUID.Zero))
                            {
                                throw new Command.ScriptException(Enumerations.ScriptError.ASSET_UPLOAD_FAILED);
                            }

                            var CreateWearableEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        KeyValue.Get(
                                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                            corradeCommandParameters.Message)),
                                    assetType,
                                    wearableUUID, InventoryType.Wearable, (WearableType)wearTypeInfo.GetValue(null),
                                    permissions == 0 ? PermissionMask.Transfer : permissions,
                                    delegate (bool completed, InventoryItem createdItem)
                                    {
                                        inventoryItem = createdItem;
                                        succeeded = completed;
                                        CreateWearableEvent.Set();
                                    });
                            if (!CreateWearableEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        case AssetType.Landmark:
                            var landmarkUUID = Client.Assets.RequestUpload(assetType, data, false);
                            if (landmarkUUID.Equals(UUID.Zero))
                            {
                                throw new Command.ScriptException(Enumerations.ScriptError.ASSET_UPLOAD_FAILED);
                            }
                            var CreateLandmarkEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                    name,
                                    wasInput(
                                        KeyValue.Get(
                                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                            corradeCommandParameters.Message)),
                                    assetType,
                                    landmarkUUID, InventoryType.Landmark, PermissionMask.All,
                                    delegate (bool completed, InventoryItem createdItem)
                                    {
                                        inventoryItem = createdItem;
                                        succeeded = completed;
                                        CreateLandmarkEvent.Set();
                                    });
                            if (!CreateLandmarkEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        case AssetType.Gesture:
                            if (inventoryItem == null)
                            {
                                Locks.ClientInstanceInventoryLock.EnterWriteLock();
                                var CreateGestureEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                        name,
                                        wasInput(
                                            KeyValue.Get(
                                                wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                                corradeCommandParameters.Message)),
                                        assetType,
                                        UUID.Random(), InventoryType.Gesture,
                                        permissions == 0 ? PermissionMask.Transfer : permissions,
                                        delegate (bool completed, InventoryItem createdItem)
                                        {
                                            inventoryItem = createdItem;
                                            succeeded = completed;
                                            CreateGestureEvent.Set();
                                        });
                                if (!CreateGestureEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                                {
                                    Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                    throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                                }
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                if (!succeeded)
                                {
                                    throw new Command.ScriptException(Enumerations.ScriptError.UNABLE_TO_CREATE_ITEM);
                                }
                            }
                            var UploadGestureAssetEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestUploadGestureAsset(data, inventoryItem.UUID,
                                    delegate (bool completed, string status, UUID itemID, UUID assetID)
                                    {
                                        inventoryItem = Client.Inventory.Store[itemID] as InventoryItem;
                                        succeeded = completed;
                                        UploadGestureAssetEvent.Set();
                                    });
                            if (!UploadGestureAssetEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_UPLOADING_ASSET);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        case AssetType.Notecard:
                            if (inventoryItem == null)
                            {
                                var CreateNotecardEvent = new ManualResetEvent(false);
                                Locks.ClientInstanceInventoryLock.EnterWriteLock();
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                        name,
                                        wasInput(
                                            KeyValue.Get(
                                                wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                                corradeCommandParameters.Message)),
                                        assetType,
                                        UUID.Random(), InventoryType.Notecard,
                                        permissions == 0 ? PermissionMask.Transfer : permissions,
                                        delegate (bool completed, InventoryItem createdItem)
                                        {
                                            inventoryItem = createdItem;
                                            succeeded = completed;
                                            CreateNotecardEvent.Set();
                                        });
                                if (!CreateNotecardEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                                {
                                    Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                    throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                                }
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                if (!succeeded)
                                {
                                    throw new Command.ScriptException(Enumerations.ScriptError.UNABLE_TO_CREATE_ITEM);
                                }

                                // Upload blank notecard.
                                AssetNotecard emptyNotecard = new AssetNotecard
                                {
                                    BodyText = wasOpenMetaverse.Constants.ASSETS.NOTECARD.NEWLINE
                                };
                                emptyNotecard.Encode();
                                var CreateBlankNotecardEvent = new ManualResetEvent(false);
                                Locks.ClientInstanceInventoryLock.EnterWriteLock();
                                Client.Inventory.RequestUploadNotecardAsset(emptyNotecard.AssetData, inventoryItem.UUID,
                                    delegate (bool completed, string status, UUID itemUUID, UUID assetUUID)
                                    {
                                        succeeded = completed;
                                        inventoryItem.UUID = itemUUID;
                                        inventoryItem.AssetUUID = assetUUID;
                                        CreateBlankNotecardEvent.Set();
                                    });
                                if (!CreateBlankNotecardEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                                {
                                    Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                    throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                                }
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                if (!succeeded)
                                {
                                    throw new Command.ScriptException(Enumerations.ScriptError.UNABLE_TO_CREATE_ITEM);
                                }
                            }
                            var UploadNotecardAssetEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestUploadNotecardAsset(data, inventoryItem.UUID,
                                    delegate (bool completed, string status, UUID itemID, UUID assetID)
                                    {
                                        inventoryItem = Client.Inventory.Store[itemID] as InventoryItem;
                                        succeeded = completed;
                                        UploadNotecardAssetEvent.Set();
                                    });
                            if (!UploadNotecardAssetEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_UPLOADING_ASSET);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        case AssetType.LSLText:
                            if (inventoryItem == null)
                            {
                                Locks.ClientInstanceInventoryLock.EnterWriteLock();
                                var CreateScriptEvent = new ManualResetEvent(false);
                                Client.Inventory.RequestCreateItem(Client.Inventory.FindFolderForType(assetType),
                                        name,
                                        wasInput(
                                            KeyValue.Get(
                                                wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DESCRIPTION)),
                                                corradeCommandParameters.Message)),
                                        assetType,
                                        UUID.Random(), InventoryType.LSL,
                                        permissions == 0 ? PermissionMask.Transfer : permissions,
                                        delegate (bool completed, InventoryItem createdItem)
                                        {
                                            inventoryItem = createdItem;
                                            succeeded = completed;
                                            CreateScriptEvent.Set();
                                        });
                                if (!CreateScriptEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                                {
                                    Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                    throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_CREATING_ITEM);
                                }
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            }

                            bool mono;
                            if (!bool.TryParse(wasInput(KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.MONO)),
                                    corradeCommandParameters.Message)), out mono))
                            {
                                mono = true;
                            }
                            var UpdateScriptEvent = new ManualResetEvent(false);
                            Locks.ClientInstanceInventoryLock.EnterWriteLock();
                            Client.Inventory.RequestUpdateScriptAgentInventory(data, inventoryItem.UUID, mono,
                                    delegate (bool completed, string status, bool compiled, List<string> messages,
                                        UUID itemID, UUID assetID)
                                    {
                                        // Add the compiler output to the return.
                                        if (!compiled)
                                            csv.AddRange(new[] {
                                                wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.ERROR)), CSV.FromEnumerable(messages),
                                            });
                                        inventoryItem = Client.Inventory.Store[itemID] as InventoryItem;
                                        succeeded = completed;
                                        UpdateScriptEvent.Set();
                                    });
                            if (!UpdateScriptEvent.WaitOne((int)corradeConfiguration.ServicesTimeout, true))
                            {
                                Locks.ClientInstanceInventoryLock.ExitWriteLock();
                                throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_UPLOADING_ASSET);
                            }
                            Locks.ClientInstanceInventoryLock.ExitWriteLock();
                            break;

                        default:
                            throw new Command.ScriptException(Enumerations.ScriptError.UNKNOWN_ASSET_TYPE);
                    }
                    if (!succeeded)
                    {
                        throw new Command.ScriptException(Enumerations.ScriptError.ASSET_UPLOAD_FAILED);
                    }

                    // Mark the containing asset folder as needing an update.
                    Locks.ClientInstanceInventoryLock.EnterWriteLock();
                    Client.Inventory.Store.GetNodeFor(Client.Inventory.FindFolderForType(assetType)).NeedsUpdate = true;
                    Locks.ClientInstanceInventoryLock.ExitWriteLock();

                    // Store the any asset in the cache.
                    if (!inventoryItem.AssetUUID.Equals(UUID.Zero))
                    {
                        Locks.ClientInstanceAssetsLock.EnterWriteLock();
                        Client.Assets.Cache.SaveAssetToCache(inventoryItem.AssetUUID, data);
                        Locks.ClientInstanceAssetsLock.ExitWriteLock();
                        if (corradeConfiguration.EnableHorde)
                            HordeDistributeCacheAsset(inventoryItem.UUID, data, Configuration.HordeDataSynchronizationOption.Add);
                    }
                    // Add the item and assetUUID ot the output.
                    csv.AddRange(new[]
                        {
                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.ITEM)), inventoryItem.UUID.ToString(),
                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.ASSET)), inventoryItem.AssetUUID.ToString()
                        });
                    // Return the item and asset UUID.
                    result.Add(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.DATA)),
                        CSV.FromEnumerable(csv));
                };
        }
    }
}
