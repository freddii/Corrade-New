///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CorradeConfiguration;
using OpenMetaverse;
using wasOpenMetaverse;
using wasSharp;
using Parallel = System.Threading.Tasks.Parallel;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<CorradeCommandParameters, Dictionary<string, string>> getterrainheight =
                (corradeCommandParameters, result) =>
                {
                    if (!HasCorradePermission(corradeCommandParameters.Group.Name, (int) Configuration.Permissions.Land))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    string region =
                        wasInput(
                            KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.REGION)),
                                corradeCommandParameters.Message));
                    Simulator simulator =
                        Client.Network.Simulators.AsParallel().FirstOrDefault(
                            o =>
                                o.Name.Equals(
                                    string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                    StringComparison.OrdinalIgnoreCase));
                    if (simulator == null)
                    {
                        throw new ScriptException(ScriptError.REGION_NOT_FOUND);
                    }
                    // Get all sim parcels
                    ManualResetEvent SimParcelsDownloadedEvent = new ManualResetEvent(false);
                    EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                        (sender, args) => SimParcelsDownloadedEvent.Set();
                    lock (Locks.ClientInstanceParcelsLock)
                    {
                        Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                        Client.Parcels.RequestAllSimParcels(simulator);
                        if (simulator.IsParcelMapFull())
                        {
                            SimParcelsDownloadedEvent.Set();
                        }
                        if (!SimParcelsDownloadedEvent.WaitOne((int) corradeConfiguration.ServicesTimeout, false))
                        {
                            Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                            throw new ScriptException(ScriptError.TIMEOUT_GETTING_PARCELS);
                        }
                        Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                    }
                    Vector3 southwest;
                    if (
                        !Vector3.TryParse(
                            wasInput(
                                KeyValue.Get(
                                    wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.SOUTHWEST)),
                                    corradeCommandParameters.Message)),
                            out southwest))
                    {
                        southwest = new Vector3(0, 0, 0);
                    }
                    Vector3 northeast;
                    if (
                        !Vector3.TryParse(
                            wasInput(
                                KeyValue.Get(
                                    wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.NORTHEAST)),
                                    corradeCommandParameters.Message)),
                            out northeast))
                    {
                        northeast = new Vector3(255, 255, 0);
                    }

                    int x1 = Convert.ToInt32(southwest.X);
                    int y1 = Convert.ToInt32(southwest.Y);
                    int x2 = Convert.ToInt32(northeast.X);
                    int y2 = Convert.ToInt32(northeast.Y);

                    if (x1 > x2)
                    {
                        BitTwiddling.XORSwap(ref x1, ref x2);
                    }
                    if (y1 > y2)
                    {
                        BitTwiddling.XORSwap(ref y1, ref y2);
                    }

                    int sx = x2 - x1 + 1;
                    int sy = y2 - y1 + 1;

                    float[] csv = new float[sx*sy];
                    Parallel.ForEach(Enumerable.Range(x1, sx), x => Parallel.ForEach(Enumerable.Range(y1, sy), y =>
                    {
                        float height;
                        csv[sx*x + y] = simulator.TerrainHeightAtPoint(x, y, out height)
                            ? height
                            : -1;
                    }));
                    if (csv.Any())
                    {
                        result.Add(Reflection.GetNameFromEnumValue(ResultKeys.DATA),
                            CSV.FromEnumerable(csv.Select(o => o.ToString(Utils.EnUsCulture))));
                    }
                };
        }
    }
}