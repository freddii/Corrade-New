﻿///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NTextCat;
using OpenMetaverse;
using wasSharp;
using Helpers = wasOpenMetaverse.Helpers;

namespace Corrade
{
    public partial class Corrade
    {
        public static partial class CorradeNotifications
        {
            public static Action<CorradeNotificationParameters, Dictionary<string, string>> local =
                (corradeNotificationParameters, notificationData) =>
                {
                    ChatEventArgs localChatEventArgs = (ChatEventArgs) corradeNotificationParameters.Event;
                    // In case we should send specific data then query the structure and return.
                    if (corradeNotificationParameters.Notification.Data != null &&
                        corradeNotificationParameters.Notification.Data.Any())
                    {
                        notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.DATA),
                            CSV.FromEnumerable(GetStructuredData(localChatEventArgs,
                                CSV.FromEnumerable(corradeNotificationParameters.Notification.Data))));
                        return;
                    }
                    IEnumerable<string> name = Helpers.GetAvatarNames(localChatEventArgs.FromName);
                    if (name != null)
                    {
                        List<string> fullName = new List<string>(name);
                        if (fullName.Count.Equals(2))
                        {
                            notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.FIRSTNAME),
                                fullName.First());
                            notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.LASTNAME),
                                fullName.Last());
                        }
                    }
                    // Message can be empty if it was not heard (out of chat range).
                    if (!string.IsNullOrEmpty(localChatEventArgs.Message))
                    {
                        notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.MESSAGE),
                            localChatEventArgs.Message);
                        // language detection
                        string profilePath = IO.PathCombine(CORRADE_CONSTANTS.LIBS_DIRECTORY,
                            CORRADE_CONSTANTS.LANGUAGE_PROFILE_FILE);
                        string mostCertainLanguage = @"Unknown";
                        if (File.Exists(profilePath))
                        {
                            Tuple<LanguageInfo, double> detectedLanguage =
                                new RankedLanguageIdentifierFactory().Load(profilePath)
                                    .Identify(localChatEventArgs.Message)
                                    .FirstOrDefault();
                            if (detectedLanguage != null)
                                mostCertainLanguage = detectedLanguage.Item1.Iso639_3;
                        }
                        notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.LANGUAGE), mostCertainLanguage);
                    }
                    notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.OWNER),
                        localChatEventArgs.OwnerID.ToString());
                    notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.ITEM),
                        localChatEventArgs.SourceID.ToString());
                    notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.POSITION),
                        localChatEventArgs.Position.ToString());
                    notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.ENTITY),
                        Enum.GetName(typeof (ChatSourceType), localChatEventArgs.SourceType));
                    notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.AUDIBLE),
                        Enum.GetName(typeof (ChatAudibleLevel), localChatEventArgs.AudibleLevel));
                    notificationData.Add(Reflection.GetNameFromEnumValue(ScriptKeys.VOLUME),
                        Enum.GetName(typeof (ChatType), localChatEventArgs.Type));
                };
        }
    }
}