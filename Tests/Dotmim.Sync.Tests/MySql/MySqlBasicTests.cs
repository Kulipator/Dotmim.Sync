
using Dotmim.Sync.Test.Misc;
using Dotmim.Sync.Tests.Core;
using Dotmim.Sync.Tests.Misc;
using Dotmim.Sync.Tests.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Dotmim.Sync.Tests.MySql
{
    /// <summary>
    /// this is the class which implements concret fixture with SqlSyncProviderFixture 
    /// and will call all the base tests
    /// </summary>
    [Collection("MySql")]
    [TestCaseOrderer("Dotmim.Sync.Tests.Misc.PriorityOrderer", "Dotmim.Sync.Tests")]
    public class MySqlBasicTests : BasicTestsBase, IClassFixture<MySqlFixture>
    {

        /// <summary>
        /// because schemas are not replicate on SQL Server, from MySQL
        /// Override get context to set "don't use schema on Sql client childs "
        /// </summary>
        protected override AdventureWorksContext GetClientDbContext(ProviderRun providerRun)
        {
            return new AdventureWorksContext(providerRun, false);
        }


        public MySqlBasicTests(MySqlFixture fixture) : base(fixture)
        {
        }

        [Fact, TestPriority(0)]
        public override Task Initialize()
        {
            return base.Initialize();
        }

        //[Fact, TestPriority(1)]
        //public override Task Bad_Server_Connection_Should_Raise_Error()
        //{
        //    return base.Bad_Server_Connection_Should_Raise_Error();
        //}

        //[Fact, TestPriority(2)]
        //public override Task Bad_Client_Connection_Should_Raise_Error()
        //{
        //    return base.Bad_Client_Connection_Should_Raise_Error();
        //}

        [Fact, TestPriority(3)]
        public override Task Insert_From_Server()
        {
            return base.Insert_From_Server();
        }

        [Fact, TestPriority(4)]
        public override Task Insert_From_Client()
        {
            return base.Insert_From_Client();
        }

        [Fact, TestPriority(5)]
        public override Task Update_From_Server()
        {
            return base.Update_From_Server();
        }

        [Fact, TestPriority(6)]
        public override Task Update_From_Client()
        {
            return base.Update_From_Client();
        }

        [Fact, TestPriority(7)]
        public override Task Delete_From_Server()
        {
            return base.Delete_From_Server();
        }


        [Fact, TestPriority(8)]
        public override Task Delete_From_Client()
        {
            return base.Delete_From_Client();
        }

        [Fact, TestPriority(9)]
        public override Task Conflict_Insert_Insert_Server_Should_Wins()
        {
            return base.Conflict_Insert_Insert_Server_Should_Wins();
        }

        [Fact, TestPriority(10)]
        public override Task Conflict_Insert_Insert_Client_Should_Wins_Coz_Configuration()
        {
            return base.Conflict_Insert_Insert_Client_Should_Wins_Coz_Configuration();
        }

        [Fact, TestPriority(11)]
        public override Task Conflict_Insert_Insert_Client_Should_Wins_Coz_Handler_Raised()
        {
            return base.Conflict_Insert_Insert_Client_Should_Wins_Coz_Handler_Raised();
        }

        [Fact, TestPriority(12)]
        public override Task Conflict_Update_Update_Client_Should_Wins_Coz_Configuration()
        {
            return base.Conflict_Update_Update_Client_Should_Wins_Coz_Configuration();
        }

        [Fact, TestPriority(13)]
        public override Task Conflict_Update_Update_Client_Should_Wins_Coz_Handler_Raised()
        {
            return base.Conflict_Update_Update_Client_Should_Wins_Coz_Handler_Raised();
        }

        [Fact, TestPriority(14)]
        public override Task Conflict_Update_Update_Resolve_By_Merge()
        {
            return base.Conflict_Update_Update_Resolve_By_Merge();
        }

        [Fact, TestPriority(15)]
        public override Task Conflict_Update_Update_Server_Should_Wins()
        {
            return base.Conflict_Update_Update_Server_Should_Wins();
        }

        [Fact, TestPriority(18)]
        public override Task Insert_Then_Delete_From_Server_Then_Sync()
        {
            return base.Insert_Then_Delete_From_Server_Then_Sync();
        }

        [Fact, TestPriority(17)]
        public override Task Insert_Then_Update_From_Server_Then_Sync()
        {
            return base.Insert_Then_Update_From_Server_Then_Sync();
        }

        [Fact, TestPriority(18)]
        public override Task Use_Existing_Client_Database_Provision_Deprosivion()
        {
            return base.Use_Existing_Client_Database_Provision_Deprosivion();
        }

        [Fact, TestPriority(21)]
        public override Task Check_Composite_ForeignKey_Existence()
        {
            return base.Check_Composite_ForeignKey_Existence();
        }
    }
}
