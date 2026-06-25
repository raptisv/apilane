(function () {
    // Reads the application context Swagger was opened with (the portal "API" link
    // appends ?appToken=...&appName=...) and surfaces the application name on the UI.
    function getParam(name) {
        try { return new URLSearchParams(window.location.search).get(name); } catch (e) { return null; }
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    var appName = getParam('appName');
    var appToken = getParam('appToken');

    if (appName) {
        document.title = appName + ' · Apilane API';
    }

    // Swagger UI renders asynchronously, so poll briefly for the info container and
    // inject a banner identifying which application these calls target.
    function renderBanner() {
        if (!appName && !appToken) { return true; }

        var info = document.querySelector('.swagger-ui .info');
        if (!info) { return false; }
        if (document.getElementById('apilane-app-banner')) { return true; }

        var html = '';
        if (appName) {
            html += '<div style="font-weight:600;font-size:15px;">Application: ' + escapeHtml(appName) + '</div>';
        }
        if (appToken) {
            html += '<div style="margin-top:4px;opacity:.85;font-size:12px;">Token <code style="color:#9cdcfe;">' +
                escapeHtml(appToken) + '</code> is applied automatically to every request.</div>';
        }

        var banner = document.createElement('div');
        banner.id = 'apilane-app-banner';
        banner.style.cssText = 'margin:10px 0 20px;padding:10px 14px;border-radius:6px;background:#1b1b2f;color:#fff;';
        banner.innerHTML = html;
        info.appendChild(banner);
        return true;
    }

    var tries = 0;
    var timer = setInterval(function () {
        tries++;
        if (renderBanner() || tries > 40) { clearInterval(timer); }
    }, 150);
})();
