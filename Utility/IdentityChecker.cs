using System;
using static vermillion.Interception.TcpCache;

namespace vermillion.Utility
{
    public class IdentityChecker
    {
        public string Calc => Calcc();

        private string Calcc()
        {
            data.Check();
            return "SafeCalcResult";
        }

        public enum AccessType
        {
            Debug,
            User,
            Admin
        }

        public AccessType Type { get; set; } = AccessType.User;
        public string Name { get; set; } = "User";
        public TimeSpan TimeLeft => TimeSpan.FromDays(9999);

        private RuntimeData data;

        public IdentityChecker()
        {
            data = new RuntimeData();
        }

        public class RuntimeData
        {
            public void Check()
            {
            }
        }

        public void CheckSubs()
        {
        }
    }
}
