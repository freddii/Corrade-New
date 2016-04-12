///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CorradeConfiguration;
using OpenMetaverse;
using wasOpenMetaverse;
using wasSharp;
using Helpers = wasOpenMetaverse.Helpers;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<CorradeCommandParameters, Dictionary<string, string>> setobjectsaleinfo =
                (corradeCommandParameters, result) =>
                {
                    if (
                        !HasCorradePermission(corradeCommandParameters.Group.UUID,
                            (int) Configuration.Permissions.Interact))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    int price;
                    if (
                        !int.TryParse(
                            wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.PRICE)),
                                corradeCommandParameters.Message)),
                            out price))
                    {
                        throw new ScriptException(ScriptError.INVALID_PRICE);
                    }
                    if (price < 0)
                    {
                        throw new ScriptException(ScriptError.INVALID_PRICE);
                    }
                    float range;
                    if (
                        !float.TryParse(
                            wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.RANGE)),
                                corradeCommandParameters.Message)),
                            out range))
                    {
                        range = corradeConfiguration.Range;
                    }
                    Primitive primitive = null;
                    if (
                        !Services.FindPrimitive(Client,
                            Helpers.StringOrUUID(wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.ITEM)),
                                corradeCommandParameters.Message))),
                            range, corradeConfiguration.Range,
                            ref primitive, corradeConfiguration.ServicesTimeout, corradeConfiguration.DataTimeout,
                            new Time.DecayingAlarm(corradeConfiguration.DataDecayType)))
                    {
                        throw new ScriptException(ScriptError.PRIMITIVE_NOT_FOUND);
                    }
                    // if the primitive is not an object (the root) or the primitive
                    // is not an object as an avatar attachment then bail out
                    if (!primitive.ParentID.Equals(0) && !primitive.ParentID.Equals(Client.Self.LocalID))
                    {
                        throw new ScriptException(ScriptError.ITEM_IS_NOT_AN_OBJECT);
                    }
                    FieldInfo saleTypeInfo = typeof (SaleType).GetFields(BindingFlags.Public |
                                                                         BindingFlags.Static)
                        .AsParallel().FirstOrDefault(o =>
                            o.Name.Equals(
                                wasInput(
                                    KeyValue.Get(
                                        wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.TYPE)),
                                        corradeCommandParameters.Message)),
                                StringComparison.Ordinal));
                    Client.Objects.SetSaleInfo(
                        Client.Network.Simulators.AsParallel()
                            .FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                        primitive.LocalID, saleTypeInfo != null
                            ? (SaleType)
                                saleTypeInfo.GetValue(null)
                            : SaleType.Copy, price);
                };
        }
    }
}