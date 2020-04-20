﻿using HelpDeskTeamProject.Classes;
using HelpDeskTeamProject.Context;
using HelpDeskTeamProject.DataModels;
using HelpDeskTeamProject.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;

namespace HelpDeskTeamProject.Services
{
    public class TeamService : ITeamService
    {
        private IAppContext db;
        private string TEAM_OWNER_ROLE_NAME = WebConfigurationManager.AppSettings["TeamOwnerRoleName"];
        private string DEFAULT_TEAM_ROLE_NAME = WebConfigurationManager.AppSettings["DefaultTeamRoleName"];

        public TeamService(IAppContext context)
        {
            db = context;
        }

        public List<TeamMenuItem> GetUserTeamsList(User user)
        {
            var userTeamsList = db.Teams
               .Where(t => t.Users.Select(u => u.Id).Contains(user.Id))
               .ToList();

            var teamMenu = new List<TeamMenuItem>();

            foreach(var team in userTeamsList)
            {
                var teamMenuItem = new TeamMenuItem()
                {
                    TeamId = team.Id,
                    TeamName = team.Name
                };

                teamMenu.Add(teamMenuItem);
            }

            return teamMenu;
        }


        public List<TeamMenuItem> GetTeamsList()
        {
            var teamsList = db.Teams
               .ToList();

            var teamMenu = new List<TeamMenuItem>();

            foreach (var team in teamsList)
            {
                var teamMenuItem = new TeamMenuItem()
                {
                    TeamId = team.Id,
                    TeamName = team.Name
                };

                teamMenu.Add(teamMenuItem);
            }

            return teamMenu;
        }

        public Team CreateTeamWithOwner(string teamName, User ownerOfTeam)
        {
            Team createdTeam = CreateTeam(teamName, ownerOfTeam.Id);
            createdTeam.Users.Add(ownerOfTeam);
            db.Teams.Add(createdTeam);
            db.SaveChanges();

            return createdTeam;
        }

        private Team CreateTeam(string teamName, int ownerId)
        {
            Guid teamGuid = Guid.NewGuid();

            var owner = db.Users.Find(ownerId);

            var request = HttpContext.Current.Request;
            string siteUrl = $"{request.Url.Scheme}://{request.Url.Authority}";

            Team team = new Team()
            {
                Name = teamName,
                OwnerId = ownerId,
                TeamGuid = teamGuid,
                InvitationLink = siteUrl + "/teams/jointeam/" + teamGuid.ToString() + "/",
                InvitedUsers = new List<InvitedUser>(),
                UserPermissions = new List<UserPermission>(),
                Tickets = new List<Ticket>(),
                Users = new List<User>()
            };
            

            var ownerRole = db.TeamRoles
               .Where(tr => tr.Name == TEAM_OWNER_ROLE_NAME)
               .FirstOrDefault();

            if (ownerRole == null)
                ownerRole = CreateTeamOwnerRole();  

            var ownerPermission = new UserPermission()
            {
                TeamRole = ownerRole,
                User = owner,
                TeamId = team.Id
            };

            team.UserPermissions.Add(ownerPermission);

            return team;
        }

        private TeamRole CreateTeamOwnerRole()
        {
            return new TeamRole()
            {
                Name = TEAM_OWNER_ROLE_NAME,
                Permissions = new TeamPermissions()
                {
                    CanInviteToTeam = true,
                    CanChangeTicketState = true,
                    CanCommentTicket = true,
                    CanCreateTicket = true,
                    CanDeleteComments = true,
                    CanDeleteTickets = true,
                    CanEditComments = true,
                    CanEditTickets = true,
                    CanSetUserRoles = true
                }
            };
        }

        public int GetTeamOwnerId(int teamId)
        {
            var team = db.Teams.Find(teamId);
            return team.OwnerId;
        }

        public ManageTeamViewModel CreateManageTeamViewModel(int teamId)
        {
            var team = db.Teams.Find(teamId);

            var teamMembers = team.Users;

            var viewModel = new ManageTeamViewModel()
            {
                Team = team,
                TeamMembers = new List<TeamMemberInfo>()
            };

            foreach (var teamMember in teamMembers)
            {
                if (teamMember.Id == team.OwnerId)
                    continue;

                var teamRole = team.UserPermissions
                    .Where(permission => permission.User == teamMember && permission.TeamId == team.Id)
                    .FirstOrDefault().TeamRole;

                var availableTeamRoles = db.TeamRoles
                    .Where(role => role.Name != TEAM_OWNER_ROLE_NAME).
                    ToList();

                var selectListForAvailableTeamRoles
                    = new SelectList(availableTeamRoles, "Id", "Name", teamRole.Id);

                var memberInfo = new TeamMemberInfo()
                {
                    TeamMember = teamMember,
                    TeamRole = teamRole,
                    AvailableTeamRoles = selectListForAvailableTeamRoles
                };

                viewModel.TeamMembers.Add(memberInfo);
            }
            return viewModel;
        }

        public bool ChangeUserRoleInTeam(int userId, int teamId, int newRoleId, User currentUser)
        {
            var team = db.Teams.Find(teamId);

            var user = db.Users.Find(userId);

            var role = db.TeamRoles.Find(newRoleId);

            if (team == null || user == null || role == null)
                return false;

            var currentUserRole = team.UserPermissions
                .Where(perm => perm.User == currentUser && perm.TeamId == team.Id)
                .FirstOrDefault()
                .TeamRole;

            if (!currentUserRole.Permissions.CanSetUserRoles)
                return false;

            var userPermission = team.UserPermissions
                .Where(up => up.User == user && up.TeamId == team.Id)
                .FirstOrDefault();

            if (userPermission == null)
                return false;

            if (userPermission.TeamRole != role)
                userPermission.TeamRole = role;

            db.SaveChanges();

            return true;
        }

        public bool DeleteUserFromTeam(int userId, int teamId)
        {
            var team = db.Teams.Find(teamId);

            var user = db.Users.Find(userId);

            if (team == null || user == null)
                return false;

            var userPermission = team.UserPermissions
                .Where(up => up.User == user && up.TeamId == team.Id)
                .FirstOrDefault();            

            var deletedUser = GetDeletedUser();            

            var userTicketsInTeam = db.Tickets
                .Where(ticket => ticket.User.Id == user.Id && ticket.TeamId == teamId)
                .ToList();

            foreach(var ticket in userTicketsInTeam)
            {
                ticket.User = deletedUser;
                var logs = ticket.Logs;
                foreach(var log in logs)
                {
                    log.User = deletedUser;
                }
            }

            var userCommentsInTeam = db.Comments
                .Where(comment => comment.TeamId == teamId && comment.User.Id == user.Id)
                .ToList();

            foreach(var comment in userCommentsInTeam)
            {
                comment.User = deletedUser;
            }

            team.UserPermissions.Remove(userPermission);
            user.Teams.Remove(team);
            team.Users.Remove(user);
            db.Permissions.Remove(userPermission);
            db.SaveChanges();

            return true;
        }

        private User GetDeletedUser()
        {
            var deletedUser = db.Users
                .Where(user => user.Name == "Deleted" && user.Surname == "User" && user.AppId == null)
                .FirstOrDefault();

            if (deletedUser == null)
                deletedUser = CreateDeletedUser();

            return deletedUser;
        }

        private User CreateDeletedUser()
        {
            ApplicationRole defaultRole = db.AppRoles.SingleOrDefault(x => x.Name.Equals("default-user"));
            if (defaultRole == null)
            {
                defaultRole = new ApplicationRole("default-user", new ApplicationPermissions(false, false, false, false, false, false, true, false));
            }

            User deletedUser = new User()
            {
                AppId = null,
                Email = "deleted@gmail.com",
                IsAdmin = false,
                IsBanned = false,
                Name = "Deleted",
                Surname = "User",
                Teams = new List<Team>(),
                AppRole = defaultRole
            };

            db.Users.Add(deletedUser);
            db.SaveChanges();

            return deletedUser;
        }

        public bool IsUserAbleToManageTeam(int teamId, User user)
        {
            var team = db.Teams.Find(teamId);

            if (team == null)
                return false;

            if (team.OwnerId != user.Id)
                return false;

            return true;
        }

        public string InviteUserToTeam(int teamId, string email)
        {
            var team = db.Teams.Find(teamId);

            bool isUserAlreadyInvited = team.InvitedUsers
                .Where(user => user.Email.ToLower() == email.ToLower()).
                Count() > 0;

            bool isUserAlreadyTeamMember = team.Users
                .Where(user => user.Email.ToLower() == email.ToLower())
                .Count() > 0;

            if (!isUserAlreadyInvited && !isUserAlreadyTeamMember)
            {
                var userCode = GenerateInvitationCode();

                var invitedUser = new InvitedUser()
                {
                    Email = email,
                    Code = userCode,
                    TimeOfLastInvitation = DateTime.Now
                };

                team.InvitedUsers.Add(invitedUser);
                db.SaveChanges();

                string invitationLink = team.InvitationLink + invitedUser.Id.ToString();
                string emailMessage = CreateInvitationEmailMessage(team.Name, invitationLink, userCode);

                SendInvitationEmail(email, emailMessage);

                return "";
            }

            if (isUserAlreadyInvited)
                return "User with this email address is already invited to team.";

            return "User with this email address is already in team.";
        }

        public bool ReinviteUserToTeam(int invUserId, int teamId, DateTime currentTime)
        {
            var team = db.Teams.Find(teamId);
            var invitedUser = team.InvitedUsers.Find(user => user.Id == invUserId);

            if (invitedUser == null)
                return false;

            var invitedEarlierThanDayAgo = currentTime - invitedUser.TimeOfLastInvitation > TimeSpan.FromHours(24);


            if (invitedEarlierThanDayAgo)
            {
                string invitationLink = team.InvitationLink + invitedUser.Id.ToString();
                string emailMessage = CreateInvitationEmailMessage(team.Name, invitationLink, invitedUser.Code);

                SendInvitationEmail(invitedUser.Email, emailMessage);
                invitedUser.TimeOfLastInvitation = currentTime;
                db.SaveChanges();

                return true;
            }

            return false;
        }

        private void SendInvitationEmail(string emailTo, string emailText)
        {
            string emailServiceLogin = WebConfigurationManager.AppSettings["EmailServiceLogin"];
            string emailServicePassword = WebConfigurationManager.AppSettings["EmailServicePassword"];

            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(emailServiceLogin, emailServicePassword),
                EnableSsl = true
            };

            client.Send(emailServiceLogin, emailTo, "Help Desk Invitation To The Team", emailText);
        }

        private string CreateInvitationEmailMessage(string teamName, string invitationLink, int code)
        {
            StringBuilder message = new StringBuilder();
            message.AppendLine("Dear Sir/Madam,");
            message.AppendLine();
            message.AppendLine($"You were invited to the team \"{teamName}\".");
            message.AppendLine($"To join the team follow the link: {invitationLink}");
            message.AppendLine($"Your code: {code}");
            message.AppendLine("If you are not interested in our service, delete this mail.");
            message.AppendLine();
            message.AppendLine("Yours faithfully,");
            message.AppendLine("Help Desk Service");
            return message.ToString();
        }

        private int GenerateInvitationCode()
        {
            Random random = new Random();
            return random.Next(100000, 999999);
        }

        public InviteUserToTeamViewModel CreateInviteUserToTeamViewModel(int teamId, User currentUser)
        {
            var team = db.Teams.Find(teamId);

            if (team == null || team.OwnerId != currentUser.Id)
                return null;

            var currentUserRole = team.UserPermissions
                .Where(perm => perm.User == currentUser && perm.TeamId == teamId)
                .FirstOrDefault()
                .TeamRole;

            if (!currentUserRole.Permissions.CanInviteToTeam)
                return null;

            var viewModel = new InviteUserToTeamViewModel()
            {
                TeamToInvite = team
            };

            return viewModel;
        }

        public JoinTeamViewModel CreateJoinTeamViewModel(Guid teamGuid, int invitedUserId)
        {
            var team = db.Teams
               .Where(t => t.TeamGuid == teamGuid)
               .FirstOrDefault();

            if (team == null)
                return null;

            var invitedUser = team.InvitedUsers.Find(iu => iu.Id == invitedUserId);

            if (invitedUser == null)
                return null;

            invitedUser.Code = 0;

            var viewModel = new JoinTeamViewModel()
            {
                InvitedUser = invitedUser,
                TeamId = team.Id,
                TeamName = team.Name
            };

            return viewModel;
        }

        public List<Team> GetAllTeams()
        {
            return db.Teams.ToList();
        }

        public string JoinTeam(int teamId, int invitedUserId, int enteredCode, User currentUser)
        {
            var team = db.Teams.Find(teamId);

            var invitedUser = team.InvitedUsers
                    .Find(iu => iu.Id == invitedUserId);

            if (invitedUser == null)
                return "User is not invited to team.";

            if (invitedUser.Code == enteredCode
                && currentUser.Email.ToLower() == invitedUser.Email.ToLower()
                && !currentUser.Teams.Contains(team) && !team.Users.Contains(currentUser))
            {
                team.Users.Add(currentUser);
                team.InvitedUsers.Remove(invitedUser);

                var defaultTeamRole = GetDefaultTeamRole();

                var defaultUserPermission = new UserPermission()
                {
                    TeamId = team.Id,
                    User = currentUser,
                    TeamRole = defaultTeamRole
                };

                team.UserPermissions.Add(defaultUserPermission);
                currentUser.Teams.Add(team);

                db.SaveChanges();

                return "";
            }

            if (invitedUser.Code != enteredCode)
                return "Code does not match.";

            if (currentUser.Email.ToLower() != invitedUser.Email.ToLower())
                return "Your current email does not match email which refer to this invitation.";

            if (currentUser.Teams.Contains(team) || team.Users.Contains(currentUser))
                return "You are already in team.";

            return "Unknown error";

        }

        private TeamRole GetDefaultTeamRole()
        {
            var defaultTeamRole = db.TeamRoles
                        .Where(role => role.Name == DEFAULT_TEAM_ROLE_NAME)
                        .FirstOrDefault();

            if (defaultTeamRole == null)
                defaultTeamRole = CreateDefaultTeamRole();

            return defaultTeamRole;
        }

        private TeamRole CreateDefaultTeamRole()
        {
            return new TeamRole()
            {
                Name = DEFAULT_TEAM_ROLE_NAME,
                Permissions = new TeamPermissions()
                { CanCommentTicket = true, CanCreateTicket = true }
            };
        }

        public User GetCurrentUser(string currentAppUserId)
        {
            var currentUser = db.Users
                .Where(user => user.AppId == currentAppUserId)
                .FirstOrDefault();

            return currentUser;
        }

        public bool TeamExists(int teamId)
        {
            var team = db.Teams.Find(teamId);

            return team != null;                
        }


    }

}