using Microsoft.AspNetCore.Mvc;
using PriceSafari.Enums;

namespace PriceSafari.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RequireUserAccessAttribute : TypeFilterAttribute
    {
        public RequireUserAccessAttribute(UserAccessRequirement requirement) : base(typeof(RequireUserAccessFilter))
        {
            Arguments = new object[] { requirement };
        }
    }
}
