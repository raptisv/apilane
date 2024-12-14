using Apilane.Common.Enums;
using Apilane.Common.Models;

namespace Apilane.Portal.Models
{
    public class SecurityItem_DTO : DBWS_Entity
    {
        public SecurityTypes TypeID { get; set; }
        public int WidthPercent
        {
            get
            {
                int count = 2;
                if (AllowPost())
                    count++;
                if (AllowPut())
                    count++;
                if (AllowDelete())
                    count++;

                if (count == 3)
                    return 33;
                if (count == 4)
                    return 25;
                if (count >= 5)
                    return 17;

                // Default
                return 50;
            }
        }
    }
}
