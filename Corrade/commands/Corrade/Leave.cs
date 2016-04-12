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

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<CorradeCommandParameters, Dictionary<string, string>> leave =
                (corradeCommandParameters, result) =>
                {
                    if (
                        !HasCorradePermission(corradeCommandParameters.Group.UUID, (int) Configuration.Permissions.Group))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    IEnumerable<UUID> currentGroups = Enumerable.Empty<UUID>();
                    if (
                        !Services.GetCurrentGroups(Client, corradeConfiguration.ServicesTimeout,
                            ref currentGroups))
                    {
                        throw new ScriptException(ScriptError.COULD_NOT_GET_CURRENT_GROUPS);
                    }
                    if (!new HashSet<UUID>(currentGroups).Contains(corradeCommandParameters.Group.UUID))
                    {
                        throw new ScriptException(ScriptError.NOT_IN_GROUP);
                    }
                    ManualResetEvent GroupLeaveReplyEvent = new ManualResetEvent(false);
                    bool succeeded = false;
                    EventHandler<GroupOperationEventArgs> GroupOperationEventHandler = (sender, args) =>
                    {
                        succeeded = args.Success;
                        GroupLeaveReplyEvent.Set();
                    };
                    lock (Locks.ClientInstanceGroupsLock)
                    {
                        Client.Groups.GroupLeaveReply += GroupOperationEventHandler;
                        Client.Groups.LeaveGroup(corradeCommandParameters.Group.UUID);
                        if (!GroupLeaveReplyEvent.WaitOne((int) corradeConfiguration.ServicesTimeout, false))
                        {
                            Client.Groups.GroupLeaveReply -= GroupOperationEventHandler;
                            throw new ScriptException(ScriptError.TIMEOUT_LEAVING_GROUP);
                        }
                        Client.Groups.GroupLeaveReply -= GroupOperationEventHandler;
                    }
                    if (!succeeded)
                    {
                        throw new ScriptException(ScriptError.COULD_NOT_LEAVE_GROUP);
                    }
                };
        }
    }
}