﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace HelpDeskTeamProject.DataModels
{
    public enum AdminAction
    {
        Login,
        BlockUser,
        ChangeUserRole,
        EditTicket,
        ChangedUserData
    }

    public class AdminLog : Log
    {

    }
}