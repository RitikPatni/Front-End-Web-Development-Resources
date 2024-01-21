﻿/*
Technitium DNS Server
Copyright (C) 2023  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using DnsServerCore.Auth;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace DnsServerCore
{
    sealed class WebServiceAuthApi
    {
        #region variables

        readonly DnsWebService _dnsWebService;

        #endregion

        #region constructor

        public WebServiceAuthApi(DnsWebService dnsWebService)
        {
            _dnsWebService = dnsWebService;
        }

        #endregion

        #region private

        private void WriteCurrentSessionDetails(Utf8JsonWriter jsonWriter, UserSession currentSession, bool includeInfo)
        {
            if (currentSession.Type == UserSessionType.ApiToken)
            {
                jsonWriter.WriteString("username", currentSession.User.Username);
                jsonWriter.WriteString("tokenName", currentSession.TokenName);
                jsonWriter.WriteString("token", currentSession.Token);
            }
            else
            {
                jsonWriter.WriteString("displayName", currentSession.User.DisplayName);
                jsonWriter.WriteString("username", currentSession.User.Username);
                jsonWriter.WriteString("token", currentSession.Token);
            }

            if (includeInfo)
            {
                jsonWriter.WritePropertyName("info");
                jsonWriter.WriteStartObject();

                jsonWriter.WriteString("version", _dnsWebService.GetServerVersion());
                jsonWriter.WriteString("uptimestamp", _dnsWebService._uptimestamp);
                jsonWriter.WriteString("dnsServerDomain", _dnsWebService.DnsServer.ServerDomain);
                jsonWriter.WriteNumber("defaultRecordTtl", _dnsWebService._zonesApi.DefaultRecordTtl);
                jsonWriter.WriteBoolean("useSoaSerialDateScheme", _dnsWebService.DnsServer.AuthZoneManager.UseSoaSerialDateScheme);
                jsonWriter.WriteBoolean("dnssecValidation", _dnsWebService.DnsServer.DnssecValidation);

                jsonWriter.WritePropertyName("permissions");
                jsonWriter.WriteStartObject();

                for (int i = 1; i <= 11; i++)
                {
                    PermissionSection section = (PermissionSection)i;

                    jsonWriter.WritePropertyName(section.ToString());
                    jsonWriter.WriteStartObject();

                    jsonWriter.WriteBoolean("canView", _dnsWebService._authManager.IsPermitted(section, currentSession.User, PermissionFlag.View));
                    jsonWriter.WriteBoolean("canModify", _dnsWebService._authManager.IsPermitted(section, currentSession.User, PermissionFlag.Modify));
                    jsonWriter.WriteBoolean("canDelete", _dnsWebService._authManager.IsPermitted(section, currentSession.User, PermissionFlag.Delete));

                    jsonWriter.WriteEndObject();
                }

                jsonWriter.WriteEndObject();

                jsonWriter.WriteEndObject();
            }
        }

        private void WriteUserDetails(Utf8JsonWriter jsonWriter, User user, UserSession currentSession, bool includeMoreDetails, bool includeGroups)
        {
            jsonWriter.WriteString("displayName", user.DisplayName);
            jsonWriter.WriteString("username", user.Username);
            jsonWriter.WriteBoolean("disabled", user.Disabled);
            jsonWriter.WriteString("previousSessionLoggedOn", user.PreviousSessionLoggedOn);
            jsonWriter.WriteString("previousSessionRemoteAddress", user.PreviousSessionRemoteAddress.ToString());
            jsonWriter.WriteString("recentSessionLoggedOn", user.RecentSessionLoggedOn);
            jsonWriter.WriteString("recentSessionRemoteAddress", user.RecentSessionRemoteAddress.ToString());

            if (includeMoreDetails)
            {
                jsonWriter.WriteNumber("sessionTimeoutSeconds", user.SessionTimeoutSeconds);

                jsonWriter.WritePropertyName("memberOfGroups");
                jsonWriter.WriteStartArray();

                List<Group> memberOfGroups = new List<Group>(user.MemberOfGroups);
                memberOfGroups.Sort();

                foreach (Group group in memberOfGroups)
                {
                    if (group.Name.Equals("Everyone", StringComparison.OrdinalIgnoreCase))
                        continue;

                    jsonWriter.WriteStringValue(group.Name);
                }

                jsonWriter.WriteEndArray();

                jsonWriter.WritePropertyName("sessions");
                jsonWriter.WriteStartArray();

                List<UserSession> sessions = _dnsWebService._authManager.GetSessions(user);
                sessions.Sort();

                foreach (UserSession session in sessions)
                    WriteUserSessionDetails(jsonWriter, session, currentSession);

                jsonWriter.WriteEndArray();
            }

            if (includeGroups)
            {
                List<Group> groups = new List<Group>(_dnsWebService._authManager.Groups);
                groups.Sort();

                jsonWriter.WritePropertyName("groups");
                jsonWriter.WriteStartArray();

                foreach (Group group in groups)
                {
                    if (group.Name.Equals("Everyone", StringComparison.OrdinalIgnoreCase))
                        continue;

                    jsonWriter.WriteStringValue(group.Name);
                }

                jsonWriter.WriteEndArray();
            }
        }

        private static void WriteUserSessionDetails(Utf8JsonWriter jsonWriter, UserSession session, UserSession currentSession)
        {
            jsonWriter.WriteStartObject();

            jsonWriter.WriteString("username", session.User.Username);
            jsonWriter.WriteBoolean("isCurrentSession", session.Equals(currentSession));
            jsonWriter.WriteString("partialToken", session.Token.Substring(0, 16));
            jsonWriter.WriteString("type", session.Type.ToString());
            jsonWriter.WriteString("tokenName", session.TokenName);
            jsonWriter.WriteString("lastSeen", session.LastSeen);
            jsonWriter.WriteString("lastSeenRemoteAddress", session.LastSeenRemoteAddress.ToString());
            jsonWriter.WriteString("lastSeenUserAgent", session.LastSeenUserAgent);

            jsonWriter.WriteEndObject();
        }

        private void WriteGroupDetails(Utf8JsonWriter jsonWriter, Group group, bool includeMembers, bool includeUsers)
        {
            jsonWriter.WriteString("name", group.Name);
            jsonWriter.WriteString("description", group.Description);

            if (includeMembers)
            {
                jsonWriter.WritePropertyName("members");
                jsonWriter.WriteStartArray();

                List<User> members = _dnsWebService._authManager.GetGroupMembers(group);
                members.Sort();

                foreach (User user in members)
                    jsonWriter.WriteStringValue(user.Username);

                jsonWriter.WriteEndArray();
            }

            if (includeUsers)
            {
                List<User> users = new List<User>(_dnsWebService._authManager.Users);
                users.Sort();

                jsonWriter.WritePropertyName("users");
                jsonWriter.WriteStartArray();

                foreach (User user in users)
                    jsonWriter.WriteStringValue(user.Username);

                jsonWriter.WriteEndArray();
            }
        }

        private void WritePermissionDetails(Utf8JsonWriter jsonWriter, Permission permission, string subItem, bool includeUsersAndGroups)
        {
            jsonWriter.WriteString("section", permission.Section.ToString());

            if (subItem is not null)
                jsonWriter.WriteString("subItem", subItem.Length == 0 ? "." : subItem);

            jsonWriter.WritePropertyName("userPermissions");
            jsonWriter.WriteStartArray();

            List<KeyValuePair<User, PermissionFlag>> userPermissions = new List<KeyValuePair<User, PermissionFlag>>(permission.UserPermissions);

            userPermissions.Sort(delegate (KeyValuePair<User, PermissionFlag> x, KeyValuePair<User, PermissionFlag> y)
            {
                return x.Key.Username.CompareTo(y.Key.Username);
            });

            foreach (KeyValuePair<User, PermissionFlag> userPermission in userPermissions)
            {
                jsonWriter.WriteStartObject();

                jsonWriter.WriteString("username", userPermission.Key.Username);
                jsonWriter.WriteBoolean("canView", userPermission.Value.HasFlag(PermissionFlag.View));
                jsonWriter.WriteBoolean("canModify", userPermission.Value.HasFlag(PermissionFlag.Modify));
                jsonWriter.WriteBoolean("canDelete", userPermission.Value.HasFlag(PermissionFlag.Delete));

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();

            jsonWriter.WritePropertyName("groupPermissions");
            jsonWriter.WriteStartArray();

            List<KeyValuePair<Group, PermissionFlag>> groupPermissions = new List<KeyValuePair<Group, PermissionFlag>>(permission.GroupPermissions);

            groupPermissions.Sort(delegate (KeyValuePair<Group, PermissionFlag> x, KeyValuePair<Group, PermissionFlag> y)
            {
                return x.Key.Name.CompareTo(y.Key.Name);
            });

            foreach (KeyValuePair<Group, PermissionFlag> groupPermission in groupPermissions)
            {
                jsonWriter.WriteStartObject();

                jsonWriter.WriteString("name", groupPermission.Key.Name);
                jsonWriter.WriteBoolean("canView", groupPermission.Value.HasFlag(PermissionFlag.View));
                jsonWriter.WriteBoolean("canModify", groupPermission.Value.HasFlag(PermissionFlag.Modify));
                jsonWriter.WriteBoolean("canDelete", groupPermission.Value.HasFlag(PermissionFlag.Delete));

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();

            if (includeUsersAndGroups)
            {
                List<User> users = new List<User>(_dnsWebService._authManager.Users);
                users.Sort();

                List<Group> groups = new List<Group>(_dnsWebService._authManager.Groups);
                groups.Sort();

                jsonWriter.WritePropertyName("users");
                jsonWriter.WriteStartArray();

                foreach (User user in users)
                    jsonWriter.WriteStringValue(user.Username);

                jsonWriter.WriteEndArray();

                jsonWriter.WritePropertyName("groups");
                jsonWriter.WriteStartArray();

                foreach (Group group in groups)
                    jsonWriter.WriteStringValue(group.Name);

                jsonWriter.WriteEndArray();
            }
        }

        #endregion

        #region public

        public async Task LoginAsync(HttpContext context, UserSessionType sessionType)
        {
            HttpRequest request = context.Request;

            string username = request.GetQueryOrForm("user");
            string password = request.GetQueryOrForm("pass");
            string tokenName = (sessionType == UserSessionType.ApiToken) ? request.GetQueryOrForm("tokenName") : null;
            bool includeInfo = request.GetQueryOrForm("includeInfo", bool.Parse, false);
            IPEndPoint remoteEP = context.GetRemoteEndPoint();

            UserSession session = await _dnsWebService._authManager.CreateSessionAsync(sessionType, tokenName, username, password, remoteEP.Address, request.Headers.UserAgent);

            _dnsWebService._log.Write(remoteEP, "[" + session.User.Username + "] User logged in.");

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteCurrentSessionDetails(jsonWriter, session, includeInfo);
        }

        public void Logout(HttpContext context)
        {
            string token = context.Request.GetQueryOrForm("token");

            UserSession session = _dnsWebService._authManager.DeleteSession(token);
            if (session is not null)
            {
                _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] User logged out.");

                _dnsWebService._authManager.SaveConfigFile();
            }
        }

        public void GetCurrentSessionDetails(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();
            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteCurrentSessionDetails(jsonWriter, session, true);
        }

        public void ChangePassword(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (session.Type != UserSessionType.Standard)
                throw new DnsWebServiceException("Access was denied.");

            string password = context.Request.GetQueryOrForm("pass");

            session.User.ChangePassword(password);

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] Password was changed successfully.");

            _dnsWebService._authManager.SaveConfigFile();
        }

        public void GetProfile(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();
            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteUserDetails(jsonWriter, session.User, session, true, false);
        }

        public void SetProfile(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (session.Type != UserSessionType.Standard)
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            if (request.TryGetQueryOrForm("displayName", out string displayName))
                session.User.DisplayName = displayName;

            if (request.TryGetQueryOrForm("sessionTimeoutSeconds", int.Parse, out int sessionTimeoutSeconds))
                session.User.SessionTimeoutSeconds = sessionTimeoutSeconds;

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] User profile was updated successfully.");

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteUserDetails(jsonWriter, session.User, session, true, false);
        }

        public void ListSessions(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                throw new DnsWebServiceException("Access was denied.");

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();

            jsonWriter.WritePropertyName("sessions");
            jsonWriter.WriteStartArray();

            List<UserSession> sessions = new List<UserSession>(_dnsWebService._authManager.Sessions);
            sessions.Sort();

            foreach (UserSession activeSession in sessions)
            {
                if (!activeSession.HasExpired())
                    WriteUserSessionDetails(jsonWriter, activeSession, session);
            }

            jsonWriter.WriteEndArray();
        }

        public void CreateApiToken(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Modify))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string username = request.GetQueryOrForm("user");
            string tokenName = request.GetQueryOrForm("tokenName");

            IPEndPoint remoteEP = context.GetRemoteEndPoint();

            UserSession createdSession = _dnsWebService._authManager.CreateApiToken(tokenName, username, remoteEP.Address, request.Headers.UserAgent);

            _dnsWebService._log.Write(remoteEP, "[" + session.User.Username + "] API token [" + tokenName + "] was created successfully for user: " + username);

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();

            jsonWriter.WriteString("username", createdSession.User.Username);
            jsonWriter.WriteString("tokenName", createdSession.TokenName);
            jsonWriter.WriteString("token", createdSession.Token);
        }

        public void DeleteSession(HttpContext context, bool isAdminContext)
        {
            UserSession session = context.GetCurrentSession();

            if (isAdminContext)
            {
                if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Delete))
                    throw new DnsWebServiceException("Access was denied.");
            }

            string strPartialToken = context.Request.GetQueryOrForm("partialToken");
            if (session.Token.StartsWith(strPartialToken))
                throw new InvalidOperationException("Invalid operation: cannot delete current session.");

            string token = null;

            foreach (UserSession activeSession in _dnsWebService._authManager.Sessions)
            {
                if (activeSession.Token.StartsWith(strPartialToken))
                {
                    token = activeSession.Token;
                    break;
                }
            }

            if (token is null)
                throw new DnsWebServiceException("No such active session was found for partial token: " + strPartialToken);

            if (!isAdminContext)
            {
                UserSession sessionToDelete = _dnsWebService._authManager.GetSession(token);
                if (sessionToDelete.User != session.User)
                    throw new DnsWebServiceException("Access was denied.");
            }

            UserSession deletedSession = _dnsWebService._authManager.DeleteSession(token);

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] User session [" + strPartialToken + "] was deleted successfully for user: " + deletedSession.User.Username);

            _dnsWebService._authManager.SaveConfigFile();
        }

        public void ListUsers(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                throw new DnsWebServiceException("Access was denied.");

            List<User> users = new List<User>(_dnsWebService._authManager.Users);
            users.Sort();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();

            jsonWriter.WritePropertyName("users");
            jsonWriter.WriteStartArray();

            foreach (User user in users)
            {
                jsonWriter.WriteStartObject();

                WriteUserDetails(jsonWriter, user, null, false, false);

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
        }

        public void CreateUser(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Modify))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string displayName = request.QueryOrForm("displayName");
            string username = request.GetQueryOrForm("user");
            string password = request.GetQueryOrForm("pass");

            User user = _dnsWebService._authManager.CreateUser(displayName, username, password);

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] User account was created successfully with username: " + user.Username);

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteUserDetails(jsonWriter, user, null, false, false);
        }

        public void GetUserDetails(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string username = request.GetQueryOrForm("user");
            bool includeGroups = request.GetQueryOrForm("includeGroups", bool.Parse, false);

            User user = _dnsWebService._authManager.GetUser(username);
            if (user is null)
                throw new DnsWebServiceException("No such user exists: " + username);

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteUserDetails(jsonWriter, user, null, true, includeGroups);
        }

        public void SetUserDetails(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Modify))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string username = request.GetQueryOrForm("user");

            User user = _dnsWebService._authManager.GetUser(username);
            if (user is null)
                throw new DnsWebServiceException("No such user exists: " + username);

            if (request.TryGetQueryOrForm("displayName", out string displayName))
                user.DisplayName = displayName;

            if (request.TryGetQueryOrForm("newUser", out string newUsername))
                _dnsWebService._authManager.ChangeUsername(user, newUsername);

            if (request.TryGetQueryOrForm("disabled", bool.Parse, out bool disabled) && (session.User != user)) //to avoid self lockout
            {
                user.Disabled = disabled;

                if (user.Disabled)
                {
                    foreach (UserSession userSession in _dnsWebService._authManager.Sessions)
                    {
                        if (userSession.Type == UserSessionType.ApiToken)
                            continue;

                        if (userSession.User == user)
                            _dnsWebService._authManager.DeleteSession(userSession.Token);
                    }
                }
            }

            if (request.TryGetQueryOrForm("sessionTimeoutSeconds", int.Parse, out int sessionTimeoutSeconds))
                user.SessionTimeoutSeconds = sessionTimeoutSeconds;

            string newPassword = request.QueryOrForm("newPass");
            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                int iterations = request.GetQueryOrForm("iterations", int.Parse, User.DEFAULT_ITERATIONS);

                user.ChangePassword(newPassword, iterations);
            }

            string memberOfGroups = request.QueryOrForm("memberOfGroups");
            if (memberOfGroups is not null)
            {
                string[] parts = memberOfGroups.Split(',');
                Dictionary<string, Group> groups = new Dictionary<string, Group>(parts.Length);

                foreach (string part in parts)
                {
                    if (part.Length == 0)
                        continue;

                    Group group = _dnsWebService._authManager.GetGroup(part);
                    if (group is null)
                        throw new DnsWebServiceException("No such group exists: " + part);

                    groups.Add(group.Name.ToLower(), group);
                }

                //ensure user is member of everyone group
                Group everyone = _dnsWebService._authManager.GetGroup(Group.EVERYONE);
                groups[everyone.Name.ToLower()] = everyone;

                if (session.User == user)
                {
                    //ensure current admin user is member of administrators group to avoid self lockout
                    Group admins = _dnsWebService._authManager.GetGroup(Group.ADMINISTRATORS);
                    groups[admins.Name.ToLower()] = admins;
                }

                user.SyncGroups(groups);
            }

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] User account details were updated successfully for user: " + username);

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteUserDetails(jsonWriter, user, null, true, false);
        }

        public void DeleteUser(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Delete))
                throw new DnsWebServiceException("Access was denied.");

            string username = context.Request.GetQueryOrForm("user");

            if (session.User.Username.Equals(username, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid operation: cannot delete current user.");

            if (!_dnsWebService._authManager.DeleteUser(username))
                throw new DnsWebServiceException("Failed to delete user: " + username);

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] User account was deleted successfully with username: " + username);

            _dnsWebService._authManager.SaveConfigFile();
        }

        public void ListGroups(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                throw new DnsWebServiceException("Access was denied.");

            List<Group> groups = new List<Group>(_dnsWebService._authManager.Groups);
            groups.Sort();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();

            jsonWriter.WritePropertyName("groups");
            jsonWriter.WriteStartArray();

            foreach (Group group in groups)
            {
                if (group.Name.Equals("Everyone", StringComparison.OrdinalIgnoreCase))
                    continue;

                jsonWriter.WriteStartObject();

                WriteGroupDetails(jsonWriter, group, false, false);

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
        }

        public void CreateGroup(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Modify))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string groupName = request.GetQueryOrForm("group");
            string description = request.GetQueryOrForm("description", "");

            Group group = _dnsWebService._authManager.CreateGroup(groupName, description);

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] Group was created successfully with name: " + group.Name);

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteGroupDetails(jsonWriter, group, false, false);
        }

        public void GetGroupDetails(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string groupName = request.GetQueryOrForm("group");
            bool includeUsers = request.GetQueryOrForm("includeUsers", bool.Parse, false);

            Group group = _dnsWebService._authManager.GetGroup(groupName);
            if (group is null)
                throw new DnsWebServiceException("No such group exists: " + groupName);

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteGroupDetails(jsonWriter, group, true, includeUsers);
        }

        public void SetGroupDetails(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Modify))
                throw new DnsWebServiceException("Access was denied.");

            HttpRequest request = context.Request;

            string groupName = request.GetQueryOrForm("group");

            Group group = _dnsWebService._authManager.GetGroup(groupName);
            if (group is null)
                throw new DnsWebServiceException("No such group exists: " + groupName);

            if (request.TryGetQueryOrForm("newGroup", out string newGroup))
                _dnsWebService._authManager.RenameGroup(group, newGroup);

            if (request.TryGetQueryOrForm("description", out string description))
                group.Description = description;

            string members = request.QueryOrForm("members");
            if (members is not null)
            {
                string[] parts = members.Split(',');
                Dictionary<string, User> users = new Dictionary<string, User>();

                foreach (string part in parts)
                {
                    if (part.Length == 0)
                        continue;

                    User user = _dnsWebService._authManager.GetUser(part);
                    if (user is null)
                        throw new DnsWebServiceException("No such user exists: " + part);

                    users.Add(user.Username, user);
                }

                if (group.Name.Equals("administrators", StringComparison.OrdinalIgnoreCase))
                    users[session.User.Username] = session.User; //ensure current admin user is member of administrators group to avoid self lockout

                _dnsWebService._authManager.SyncGroupMembers(group, users);
            }

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] Group details were updated successfully for group: " + groupName);

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WriteGroupDetails(jsonWriter, group, true, false);
        }

        public void DeleteGroup(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Delete))
                throw new DnsWebServiceException("Access was denied.");

            string groupName = context.Request.GetQueryOrForm("group");

            if (!_dnsWebService._authManager.DeleteGroup(groupName))
                throw new DnsWebServiceException("Failed to delete group: " + groupName);

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] Group was deleted successfully with name: " + groupName);

            _dnsWebService._authManager.SaveConfigFile();
        }

        public void ListPermissions(HttpContext context)
        {
            UserSession session = context.GetCurrentSession();

            if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                throw new DnsWebServiceException("Access was denied.");

            List<Permission> permissions = new List<Permission>(_dnsWebService._authManager.Permissions);
            permissions.Sort();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();

            jsonWriter.WritePropertyName("permissions");
            jsonWriter.WriteStartArray();

            foreach (Permission permission in permissions)
            {
                jsonWriter.WriteStartObject();

                WritePermissionDetails(jsonWriter, permission, null, false);

                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
        }

        public void GetPermissionDetails(HttpContext context, PermissionSection section)
        {
            UserSession session = context.GetCurrentSession();
            HttpRequest request = context.Request;
            string strSubItem = null;

            switch (section)
            {
                case PermissionSection.Unknown:
                    if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.View))
                        throw new DnsWebServiceException("Access was denied.");

                    section = request.GetQueryOrFormEnum<PermissionSection>("section");
                    break;

                case PermissionSection.Zones:
                    if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Zones, session.User, PermissionFlag.Modify))
                        throw new DnsWebServiceException("Access was denied.");

                    strSubItem = request.GetQueryOrForm("zone").TrimEnd('.');
                    break;

                default:
                    throw new InvalidOperationException();
            }

            bool includeUsersAndGroups = request.GetQueryOrForm("includeUsersAndGroups", bool.Parse, false);

            if (strSubItem is not null)
            {
                if (!_dnsWebService._authManager.IsPermitted(section, strSubItem, session.User, PermissionFlag.View))
                    throw new DnsWebServiceException("Access was denied.");
            }

            Permission permission;

            if (strSubItem is null)
                permission = _dnsWebService._authManager.GetPermission(section);
            else
                permission = _dnsWebService._authManager.GetPermission(section, strSubItem);

            if (permission is null)
                throw new DnsWebServiceException("No permissions exists for section: " + section.ToString() + (strSubItem is null ? "" : "/" + strSubItem));

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WritePermissionDetails(jsonWriter, permission, strSubItem, includeUsersAndGroups);
        }

        public void SetPermissionsDetails(HttpContext context, PermissionSection section)
        {
            UserSession session = context.GetCurrentSession();
            HttpRequest request = context.Request;
            string strSubItem = null;

            switch (section)
            {
                case PermissionSection.Unknown:
                    if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Administration, session.User, PermissionFlag.Delete))
                        throw new DnsWebServiceException("Access was denied.");

                    section = request.GetQueryOrFormEnum<PermissionSection>("section");
                    break;

                case PermissionSection.Zones:
                    if (!_dnsWebService._authManager.IsPermitted(PermissionSection.Zones, session.User, PermissionFlag.Modify))
                        throw new DnsWebServiceException("Access was denied.");

                    strSubItem = request.GetQueryOrForm("zone").TrimEnd('.');
                    break;

                default:
                    throw new InvalidOperationException();
            }

            if (strSubItem is not null)
            {
                if (!_dnsWebService._authManager.IsPermitted(section, strSubItem, session.User, PermissionFlag.Delete))
                    throw new DnsWebServiceException("Access was denied.");
            }

            Permission permission;

            if (strSubItem is null)
                permission = _dnsWebService._authManager.GetPermission(section);
            else
                permission = _dnsWebService._authManager.GetPermission(section, strSubItem);

            if (permission is null)
                throw new DnsWebServiceException("No permissions exists for section: " + section.ToString() + (strSubItem is null ? "" : "/" + strSubItem));

            string strUserPermissions = request.QueryOrForm("userPermissions");
            if (strUserPermissions is not null)
            {
                string[] parts = strUserPermissions.Split('|');
                Dictionary<User, PermissionFlag> userPermissions = new Dictionary<User, PermissionFlag>();

                for (int i = 0; i < parts.Length; i += 4)
                {
                    if (parts[i].Length == 0)
                        continue;

                    User user = _dnsWebService._authManager.GetUser(parts[i]);
                    bool canView = bool.Parse(parts[i + 1]);
                    bool canModify = bool.Parse(parts[i + 2]);
                    bool canDelete = bool.Parse(parts[i + 3]);

                    if (user is not null)
                    {
                        PermissionFlag permissionFlag = PermissionFlag.None;

                        if (canView)
                            permissionFlag |= PermissionFlag.View;

                        if (canModify)
                            permissionFlag |= PermissionFlag.Modify;

                        if (canDelete)
                            permissionFlag |= PermissionFlag.Delete;

                        userPermissions[user] = permissionFlag;
                    }
                }

                permission.SyncPermissions(userPermissions);
            }

            string strGroupPermissions = request.QueryOrForm("groupPermissions");
            if (strGroupPermissions is not null)
            {
                string[] parts = strGroupPermissions.Split('|');
                Dictionary<Group, PermissionFlag> groupPermissions = new Dictionary<Group, PermissionFlag>();

                for (int i = 0; i < parts.Length; i += 4)
                {
                    if (parts[i].Length == 0)
                        continue;

                    Group group = _dnsWebService._authManager.GetGroup(parts[i]);
                    bool canView = bool.Parse(parts[i + 1]);
                    bool canModify = bool.Parse(parts[i + 2]);
                    bool canDelete = bool.Parse(parts[i + 3]);

                    if (group is not null)
                    {
                        PermissionFlag permissionFlag = PermissionFlag.None;

                        if (canView)
                            permissionFlag |= PermissionFlag.View;

                        if (canModify)
                            permissionFlag |= PermissionFlag.Modify;

                        if (canDelete)
                            permissionFlag |= PermissionFlag.Delete;

                        groupPermissions[group] = permissionFlag;
                    }
                }

                //ensure administrators group always has all permissions
                Group admins = _dnsWebService._authManager.GetGroup(Group.ADMINISTRATORS);
                groupPermissions[admins] = PermissionFlag.ViewModifyDelete;

                switch (section)
                {
                    case PermissionSection.Zones:
                        //ensure DNS administrators group always has all permissions
                        Group dnsAdmins = _dnsWebService._authManager.GetGroup(Group.DNS_ADMINISTRATORS);
                        groupPermissions[dnsAdmins] = PermissionFlag.ViewModifyDelete;
                        break;

                    case PermissionSection.DhcpServer:
                        //ensure DHCP administrators group always has all permissions
                        Group dhcpAdmins = _dnsWebService._authManager.GetGroup(Group.DHCP_ADMINISTRATORS);
                        groupPermissions[dhcpAdmins] = PermissionFlag.ViewModifyDelete;
                        break;
                }

                permission.SyncPermissions(groupPermissions);
            }

            _dnsWebService._log.Write(context.GetRemoteEndPoint(), "[" + session.User.Username + "] Permissions were updated successfully for section: " + section.ToString() + (string.IsNullOrEmpty(strSubItem) ? "" : "/" + strSubItem));

            _dnsWebService._authManager.SaveConfigFile();

            Utf8JsonWriter jsonWriter = context.GetCurrentJsonWriter();
            WritePermissionDetails(jsonWriter, permission, strSubItem, false);
        }

        #endregion
    }
}
