///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using CorradeConfigurationSharp;
using OpenMetaverse;
using wasOpenMetaverse;
using wasSharp;
using Reflection = wasSharp.Reflection;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static readonly Action<Command.CorradeCommandParameters, Dictionary<string, string>>
                setprimitivescale =
                    (corradeCommandParameters, result) =>
                    {
                        if (
                            !HasCorradePermission(corradeCommandParameters.Group.UUID,
                                (int) Configuration.Permissions.Interact))
                        {
                            throw new Command.ScriptException(Enumerations.ScriptError.NO_CORRADE_PERMISSIONS);
                        }
                        float range;
                        if (
                            !float.TryParse(
                                wasInput(KeyValue.Get(
                                    wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.RANGE)),
                                    corradeCommandParameters.Message)),
                                out range))
                        {
                            range = corradeConfiguration.Range;
                        }
                        bool uniform;
                        if (
                            !bool.TryParse(
                                wasInput(KeyValue.Get(
                                    wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.UNIFORM)),
                                    corradeCommandParameters.Message)),
                                out uniform))
                        {
                            uniform = true;
                        }
                        Primitive primitive = null;
                        var item = wasInput(KeyValue.Get(
                            wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.ITEM)),
                            corradeCommandParameters.Message));
                        if (string.IsNullOrEmpty(item))
                        {
                            throw new Command.ScriptException(Enumerations.ScriptError.NO_ITEM_SPECIFIED);
                        }
                        UUID itemUUID;
                        switch (UUID.TryParse(item, out itemUUID))
                        {
                            case true:
                                if (
                                    !Services.FindPrimitive(Client,
                                        itemUUID,
                                        range,
                                        ref primitive,
                                        corradeConfiguration.DataTimeout))
                                {
                                    throw new Command.ScriptException(Enumerations.ScriptError.PRIMITIVE_NOT_FOUND);
                                }
                                break;
                            default:
                                if (
                                    !Services.FindPrimitive(Client,
                                        item,
                                        range,
                                        ref primitive,
                                        corradeConfiguration.DataTimeout))
                                {
                                    throw new Command.ScriptException(Enumerations.ScriptError.PRIMITIVE_NOT_FOUND);
                                }
                                break;
                        }
                        Simulator simulator;
                        lock (Locks.ClientInstanceNetworkLock)
                        {
                            simulator = Client.Network.Simulators.AsParallel()
                                .FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle));
                        }
                        if (simulator == null)
                            throw new Command.ScriptException(Enumerations.ScriptError.REGION_NOT_FOUND);
                        Vector3 scale;
                        if (
                            !Vector3.TryParse(
                                wasInput(
                                    KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.SCALE)),
                                        corradeCommandParameters.Message)),
                                out scale))
                        {
                            throw new Command.ScriptException(Enumerations.ScriptError.INVALID_SCALE);
                        }
                        if (wasOpenMetaverse.Helpers.IsSecondLife(Client) &&
                            (scale.X < wasOpenMetaverse.Constants.PRIMITIVES.MINIMUM_SIZE_X ||
                             scale.Y < wasOpenMetaverse.Constants.PRIMITIVES.MINIMUM_SIZE_Y ||
                             scale.Z < wasOpenMetaverse.Constants.PRIMITIVES.MINIMUM_SIZE_Z ||
                             scale.X > wasOpenMetaverse.Constants.PRIMITIVES.MAXIMUM_SIZE_X ||
                             scale.Y > wasOpenMetaverse.Constants.PRIMITIVES.MAXIMUM_SIZE_Y ||
                             scale.Z > wasOpenMetaverse.Constants.PRIMITIVES.MAXIMUM_SIZE_Z))
                        {
                            throw new Command.ScriptException(
                                Enumerations.ScriptError.SCALE_WOULD_EXCEED_BUILDING_CONSTRAINTS);
                        }
                        lock (Locks.ClientInstanceObjectsLock)
                        {
                            Client.Objects.SetScale(simulator,
                                primitive.LocalID, scale, true, uniform);
                        }
                    };
        }
    }
}