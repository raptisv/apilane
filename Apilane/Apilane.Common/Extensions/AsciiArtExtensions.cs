using Apilane.Common.Enums;

namespace Apilane.Common.Extensions
{
    public static class AsciiArtExtensions
    {
        public static string ToAsciiArt(
            this HostingEnvironment hostingEnv,
            string? extra = null)
        {
            var result = hostingEnv switch
            {
                HostingEnvironment.Production => @"
     __              __   __  __  __  __      _____  __      
 /\ |__)||   /\ |\ ||_   |__)|__)/  \|  \/  \/   | |/  \|\ | 
/--\|   ||__/--\| \||__  |   | \ \__/|__/\__/\__ | |\__/| \| 
                                                             
",
                HostingEnvironment.Development => @"
     __              __   __  __     __    __  __      __    ___ 
 /\ |__)||   /\ |\ ||_   |  \|_ \  /|_ |  /  \|__)|\/||_ |\ | |  
/--\|   ||__/--\| \||__  |__/|__ \/ |__|__\__/|   |  ||__| \| |  
                                                                 
",
                _ => @"
     __              __ 
 /\ |__)||   /\ |\ ||_  
/--\|   ||__/--\| \||__ 
                        
" + hostingEnv.ToString(),
            };

            result += extra switch
            {
                "live" => @"
             ___ 
|    | \  / |__  
|___ |  \/  |___ 
                 
",
                "ready" => @"
 __   ___       __      
|__) |__   /\  |  \ \ / 
|  \ |___ /~~\ |__/  |  
                        
",
                null => string.Empty,
                _ => extra
            };

            return result;
        }
    }
}
