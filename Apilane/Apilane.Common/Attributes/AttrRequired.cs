using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Attributes
{
    public class AttrRequired : RequiredAttribute
    {
        public AttrRequired()
        {
            this.ErrorMessage = "Required";
        }
    }
}
