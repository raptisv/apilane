
/*! js-cookie v3.0.5 | MIT */
!function (e, t) { "object" == typeof exports && "undefined" != typeof module ? module.exports = t() : "function" == typeof define && define.amd ? define(t) : (e = "undefined" != typeof globalThis ? globalThis : e || self, function () { var n = e.Cookies, o = e.Cookies = t(); o.noConflict = function () { return e.Cookies = n, o } }()) }(this, (function () { "use strict"; function e(e) { for (var t = 1; t < arguments.length; t++) { var n = arguments[t]; for (var o in n) e[o] = n[o] } return e } var t = function t(n, o) { function r(t, r, i) { if ("undefined" != typeof document) { "number" == typeof (i = e({}, o, i)).expires && (i.expires = new Date(Date.now() + 864e5 * i.expires)), i.expires && (i.expires = i.expires.toUTCString()), t = encodeURIComponent(t).replace(/%(2[346B]|5E|60|7C)/g, decodeURIComponent).replace(/[()]/g, escape); var c = ""; for (var u in i) i[u] && (c += "; " + u, !0 !== i[u] && (c += "=" + i[u].split(";")[0])); return document.cookie = t + "=" + n.write(r, t) + c } } return Object.create({ set: r, get: function (e) { if ("undefined" != typeof document && (!arguments.length || e)) { for (var t = document.cookie ? document.cookie.split("; ") : [], o = {}, r = 0; r < t.length; r++) { var i = t[r].split("="), c = i.slice(1).join("="); try { var u = decodeURIComponent(i[0]); if (o[u] = n.read(c, u), e === u) break } catch (e) { } } return e ? o[e] : o } }, remove: function (t, n) { r(t, "", e({}, n, { expires: -1 })) }, withAttributes: function (n) { return t(this.converter, e({}, this.attributes, n)) }, withConverter: function (n) { return t(e({}, this.converter, n), this.attributes) } }, { attributes: { value: Object.freeze(o) }, converter: { value: Object.freeze(n) } }) }({ read: function (e) { return '"' === e[0] && (e = e.slice(1, -1)), e.replace(/(%[\dA-F]{2})+/gi, decodeURIComponent) }, write: function (e) { return encodeURIComponent(e).replace(/%(2[346BF]|3[AC-F]|40|5[BDE]|60|7[BCD])/g, decodeURIComponent) } }, { path: "/" }); return t }));

$(document).ready(function () {
    $('button[type="submit"]:not(.ignore-default-submit)').click(function () {
        $('button[type="submit"]').attr('disabled', true);
        $('button[type="submit"]').html(`<div class="spinner-border spinner-border-sm">
                                            <span class="visually-hidden">Loading...</span>
                                        </div>`);
        $('form').submit();
    });

    $.each($('.role-descr'), function (index, value) {
        var Auth = $(value).data().auth;
        var Role = $(value).data().role;
        var Record = $(value).data().record;
        var Action = $(value).data().action;

        var result = getAuthDescr(Auth, Role, Record, Action);
        $(value).html(result.Icons);
        $(value).attr('title', '<div class="text-left">' + result.Description + '<div/>');
    });

    $('.iframe-link-popup').magnificPopup({
        type: 'iframe',
        titleSrc: 'title',
        iframe: {
            markup: '<div class="mfp-iframe-scaler">' +
                '<div class="mfp-close"></div>' +
                '<div class="mfp-title" style="position:absolute;"></div>' +
                '<iframe class="mfp-iframe" frameborder="0" allowfullscreen></iframe>' +
                '</div>'
        },
        callbacks: {
            markupParse: function (template, values, item) {
                values.title = item.el.attr('title');
            },
            open: function () {
                $(this.container).find('.mfp-content').css('height', '100%');
                if (this.currItem.src.endsWith('/Key')) {
                    $(this.container).find('.mfp-content').css('max-height', '200px');
                }
            }
        }
    });

    // Theme menu
    document.querySelectorAll('[data-bs-theme-value]').forEach(value => {
        value.addEventListener('click', () => {
            const theme = value.getAttribute('data-bs-theme-value');
            document.documentElement.setAttribute('data-bs-theme', theme);
            Cookies.set('theme', theme);
            location.reload();
        });
    });

    // Tooltips
    const tooltipElements = document.querySelectorAll('[data-bs-toggle="tooltip"]');
    for (const tooltip of tooltipElements) {
        new bootstrap.Tooltip(tooltip);
    }
});


function healthCheckServer(serverId, serverUrl) {
    $.ajax({
        url: `${serverUrl}/Health/Liveness`,
        type: 'GET',
        dataType: 'json',
        contentType: 'application/json; charset=utf-8',
        success: function (stats) {
            $(`.online-indicator-${serverId},.online-indicator-${serverId} .blink`).removeClass('bg-danger').addClass('bg-success');
        },
        error: function (e) {
            $(`.online-indicator-${serverId},.online-indicator-${serverId} .blink`).removeClass('bg-success').addClass('bg-danger');
        }
    });
}

function getErrorJSON(e) {
    if (e && e.responseJSON) {
        return e.responseJSON;
    }
    else if (e && e.xhr && e.xhr.responseText) {
        return JSON.parse(e.xhr.responseText);
    }
    else if (e && e.responseText) {
        return JSON.parse(e.responseText);
    }

    return null;
}

function handleError(e) {
    console.log('ERROR', e);
}

function getValueAsMBorGB(value) {
    if (value / 1024.0 >= 1)
        return (value / 1024.0).toFixed(2) + ' GB';
    return value.toFixed(2) + ' MB';
}

function getLabelForGroup(groupBy, groupValues, includeLabelIfNeeded) {
    var result = [];

    $.each(groupBy, function (t, typeItem) {
        if (typeItem.Type === 'Date') {
            var year = -1;
            var month = -1;
            var day = -1;
            var hour = -1;
            var minute = -1;
            var second = -1;
            $.each(typeItem.Values, function (p, propertyItem) {
                $.each(propertyItem.Values, function (p, property) {
                    if (property.Sub.toLowerCase() === 'year') {
                        year = groupValues[property.Name + '_' + property.Sub];
                    } else if (property.Sub.toLowerCase() === 'month') {
                        month = groupValues[property.Name + '_' + property.Sub];
                    } else if (property.Sub.toLowerCase() === 'day') {
                        day = groupValues[property.Name + '_' + property.Sub];
                    } else if (property.Sub.toLowerCase() === 'hour') {
                        hour = groupValues[property.Name + '_' + property.Sub];
                    } else if (property.Sub.toLowerCase() === 'minute') {
                        minute = groupValues[property.Name + '_' + property.Sub];
                    } else if (property.Sub.toLowerCase() === 'second') {
                        second = groupValues[property.Name + '_' + property.Sub];
                    }
                });

                result.push([
                    (day === -1 ? '' : day + '/') +
                    (month === -1 ? '' : month + '/') +
                    (year === -1 ? '' : year + ' ') +
                    (hour === -1 ? '' : hour + (minute === -1 ? '' : ':')) +
                    (minute === -1 ? '' : minute + (second === -1 ? '' : ':')) +
                    (second === -1 ? '' : second),
                    new Date(
                        year === -1 ? 0 : year,
                        month === -1 ? 0 : month - 1,
                        day === -1 ? 0 : day,
                        hour === -1 ? 0 : hour,
                        minute === -1 ? 0 : minute,
                        second === -1 ? 0 : second,
                        0)
                ]);
            });
        } else {
            $.each(typeItem.Values, function (p, propertyItem) {
                var valueIsNumeric = !isNaN(groupValues[propertyItem.Name]);
                includeLabelIfNeeded = includeLabelIfNeeded && valueIsNumeric;

                result.push([includeLabelIfNeeded
                    ? propertyItem.Name + ' (' + groupValues[propertyItem.Name] + ')'
                    : groupValues[propertyItem.Name],
                    null// This is not used
                ]);
            });
        }
    });

    return result;
}


function normalizeQueryParameters(url) {
    const urlObj = new URL(url);
    const params = new URLSearchParams(urlObj.search);

    for (let [key, value] of params) {
        params.set(key, value);
    }

    urlObj.search = params.toString();

    return urlObj.toString();
}

function apiCall(url, appToken, portalUserAuthToken, callback, errorCallback) {
    $.ajax({
        url: url,
        type: 'GET',
        beforeSend: function (request) {
            request.setRequestHeader('Authorization', portalUserAuthToken);
            request.setRequestHeader('x-application-token', appToken);
            request.setRequestHeader('x-client-id', 'portal');
        },
        success: function (data) {
            if (callback)
                callback(data);
        },
        error: function (e) {
            if (errorCallback)
                errorCallback(e);
        }
    });
}

// ---- Multi-series report panels (shared x-axis) ----

var REPORT_PALETTE = ['#4e79a7', '#f28e2b', '#e15759', '#76b7b2', '#59a14f', '#edc948', '#b07aa1', '#ff9da7', '#9c755f', '#bab0ac'];

function loadApplicationReportPanel(reportID, appToken, portalUserAuthToken, type, seriesDefs) {

    $('#view-api-endpoint-' + reportID).off('click').on('click', function (e) {
        e.preventDefault();
        var urls = seriesDefs.map(function (s) { return s.url + '&AppToken=' + appToken; }).join('\n\n');
        Swal.fire({
            title: 'API endpoint' + (seriesDefs.length > 1 ? 's' : ''),
            html: '<textarea readonly rows="10" class="w-100 form-control">' + urls + '</textarea>',
            showCancelButton: false,
            confirmButtonText: 'Close',
            buttonsStyling: false,
            customClass: { confirmButton: 'btn btn-primary' }
        });
    });

    $('#refresh-report-' + reportID).off('click').on('click', function () {
        renderReportPanel(reportID, appToken, portalUserAuthToken, type, seriesDefs);
    });

    renderReportPanel(reportID, appToken, portalUserAuthToken, type, seriesDefs);
}

function renderReportPanel(reportID, appToken, portalUserAuthToken, type, seriesDefs) {
    var el = $('#report_' + reportID);
    el.html('<div class="text-center"><div class="spinner-border"><span class="visually-hidden">Loading...</span></div></div>');

    if (!seriesDefs || seriesDefs.length === 0) {
        el.html('<div class="text-center"><span class="text-muted">No series</span></div>');
        return;
    }

    var results = new Array(seriesDefs.length);
    var remaining = seriesDefs.length;
    var hasError = false;

    seriesDefs.forEach(function (s, idx) {
        apiCall(s.url, appToken, portalUserAuthToken, function (response) {
            results[idx] = { def: s, response: response };
            remaining--;
            if (remaining === 0 && !hasError) {
                try {
                    renderCombinedReport(reportID, type, results);
                } catch (err) {
                    console.error(err);
                    el.html('<div style="text-align:center;"><span class="label label-danger">' + err + '</span></div>');
                }
            }
        }, function (e) {
            if (hasError) return;
            hasError = true;
            var msg = 'Error';
            if (e.responseJSON) { msg = e.responseJSON.Message; }
            else if (e.xhr && e.xhr.responseText) { try { msg = JSON.parse(e.xhr.responseText).Message; } catch (x) { } }
            el.html('<div style="text-align:center;"><span class="label label-danger">' + msg + '</span></div>');
        });
    });
}

// Case-insensitive lookup of an aggregate column on a response row (the column casing can vary
// across database providers, e.g. ID_Count vs ID_count).
function getRowValue(row, col) {
    if (row[col] !== undefined) { return row[col]; }
    var lc = col.toLowerCase();
    for (var k in row) {
        if (k.toLowerCase() === lc) { return row[k]; }
    }
    return undefined;
}

function extractSeriesPoints(response, groupByForChart, propertyRaw) {
    var col = propertyRaw.replace('.', '_');
    var points = [];
    $.each(response, function (i, row) {
        var labelParts = getLabelForGroup(groupByForChart, row);
        var key = labelParts.map(function (p) { return p[0]; }).join(' / ');
        var sortVal = (labelParts.length > 0 && labelParts[0][1] !== null) ? labelParts[0][1].getTime() : key;
        points.push({ key: key, sortVal: sortVal, value: getRowValue(row, col) });
    });
    return points;
}

function renderCombinedReport(reportID, type, results) {
    var el = $('#report_' + reportID);

    var allEmpty = results.every(function (r) { return !r.response || r.response.length === 0; });
    if (allEmpty) {
        el.html('<div class="text-center"><span class="text-muted" style="position:absolute; top:50%;">No data</span></div>');
        return;
    }

    // Build the shared, aligned x-axis across all series (each series uses its own group-by).
    var keyOrder = [];
    var seenKeys = {};
    var perSeriesMaps = results.map(function (r) {
        var propRaw = (r.def.properties && r.def.properties.length > 0) ? r.def.properties[0].Raw : null;
        var map = {};
        if (propRaw) {
            extractSeriesPoints(r.response, r.def.groupBy, propRaw).forEach(function (pt) {
                map[pt.key] = pt.value;
                if (!seenKeys[pt.key]) { seenKeys[pt.key] = true; keyOrder.push({ key: pt.key, sortVal: pt.sortVal }); }
            });
        }
        return map;
    });

    keyOrder.sort(function (a, b) {
        if (typeof a.sortVal === 'number' && typeof b.sortVal === 'number') { return a.sortVal - b.sortVal; }
        return ('' + a.sortVal).localeCompare('' + b.sortVal);
    });
    var labels = keyOrder.map(function (k) { return k.key; });

    if (type === 'Grid') {
        var html = '<div class="table-responsive" style="height:100%;"><table class="table table-sm table-hover table-report-item"><thead><tr><th>Group</th>';
        results.forEach(function (r) { html += '<th>' + r.def.label + '</th>'; });
        html += '</tr></thead><tbody>';
        labels.forEach(function (lab) {
            html += '<tr><td style="font-weight:bold;">' + lab + '</td>';
            perSeriesMaps.forEach(function (m) { html += '<td>' + (m[lab] !== undefined && m[lab] !== null ? m[lab] : '') + '</td>'; });
            html += '</tr>';
        });
        html += '</tbody></table></div>';
        el.html(html);
        return;
    }

    el.html('<canvas id="report_' + reportID + '_chart"></canvas>');
    var ctx = document.getElementById('report_' + reportID + '_chart').getContext('2d');

    if (type === 'Pie') {
        // A pie shows a single series; slices are the group values of the first series.
        var first = perSeriesMaps[0];
        new Chart(ctx, {
            type: 'pie',
            data: {
                labels: labels,
                datasets: [{
                    data: labels.map(function (lab) { return toNumberOrNull(first[lab]); }),
                    backgroundColor: labels.map(function (lab, i) { return REPORT_PALETTE[i % REPORT_PALETTE.length]; })
                }]
            },
            options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: true, position: 'right' } } }
        });
        return;
    }

    // Line / Bar / Stacked bar / Radar: one dataset per series, aligned to the shared labels.
    var chartType = (type === 'Bar' || type === 'StackedBar') ? 'bar' : (type === 'Radar' ? 'radar' : 'line');
    var isRadar = chartType === 'radar';
    var isStacked = type === 'StackedBar';

    var datasets = results.map(function (r, idx) {
        var m = perSeriesMaps[idx];
        var color = REPORT_PALETTE[idx % REPORT_PALETTE.length];
        return {
            label: r.def.label,
            data: labels.map(function (lab) { return toNumberOrNull(m[lab]); }),
            borderColor: color,
            borderWidth: 2,
            // Radar fills a translucent polygon; line/bar use the solid series color.
            backgroundColor: isRadar ? hexWithAlpha(color, 0.3) : color,
            pointBackgroundColor: color,
            pointRadius: isRadar ? 2 : 3,
            fill: isRadar,
            spanGaps: true
        };
    });

    new Chart(ctx, {
        type: chartType,
        data: { labels: labels, datasets: datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            // Line/bar: tooltip on hover anywhere along the x-axis (all series at that x).
            // Radar: hover the nearest point (index mode is meaningless on a radial axis).
            interaction: isRadar ? { mode: 'nearest', intersect: true } : { mode: 'index', intersect: false },
            plugins: { legend: { display: true, position: 'bottom' } },
            // Soft minimum of 0 (radar uses the radial 'r' scale; hide its numeric value ticks).
            // Stacked bar stacks both axes so series sum per x-position.
            scales: isRadar
                ? { r: { suggestedMin: 0, ticks: { display: false } } }
                : (isStacked
                    ? { x: { stacked: true }, y: { stacked: true, suggestedMin: 0 } }
                    : { y: { suggestedMin: 0 } })
        }
    });
}

// Coerces a value to a number, or null when it is missing / not numeric.
function toNumberOrNull(v) {
    if (v === undefined || v === null || v === '') { return null; }
    var n = Number(v);
    return isNaN(n) ? null : n;
}

// Converts a #rrggbb colour to an rgba() string with the given alpha.
function hexWithAlpha(hex, alpha) {
    var n = parseInt(hex.slice(1), 16);
    return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + alpha + ')';
}

function loadApplicationDisplayToken(appId, appToken, appEncryptionKey) {
    $('.swal-display-token-' + appId).click(function () {
        // Read-only inputs so the values can be selected/copied (a <b> tag cannot be
        // reliably double-click selected), each with a one-click copy button.
        function copyField(label, value) {
            return '<div class="mb-3 text-start">' +
                '<label class="form-label mb-1">' + label + '</label>' +
                '<div class="input-group">' +
                '<input type="text" class="form-control" readonly value="' + value + '" onclick="this.select();" />' +
                '<button type="button" class="btn btn-outline-secondary copy-btn" data-copy="' + value + '" title="Copy">' +
                '<i class="bi bi-clipboard"></i></button>' +
                '</div></div>';
        }

        Swal.fire({
            title: "Application info",
            html: copyField('Token', appToken) + copyField('Encryption key', appEncryptionKey),
            showCancelButton: false,
            showConfirmButton: true,
            confirmButtonText: "OK",
            allowOutsideClick: true,
            buttonsStyling: false,
            customClass: {
                confirmButton: 'btn btn-primary'
            },
            didOpen: function (popup) {
                popup.querySelectorAll('.copy-btn').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        var value = btn.getAttribute('data-copy');
                        var flash = function () {
                            var icon = btn.querySelector('i');
                            if (!icon) { return; }
                            icon.className = 'bi bi-check2';
                            setTimeout(function () { icon.className = 'bi bi-clipboard'; }, 1200);
                        };
                        if (navigator.clipboard && navigator.clipboard.writeText) {
                            navigator.clipboard.writeText(value).then(flash).catch(function () { });
                        } else {
                            var input = btn.closest('.input-group').querySelector('input');
                            input.select();
                            document.execCommand('copy');
                            flash();
                        }
                    });
                });
            }
        });
    });
}

function generateApplicationKey(count) {
    var text = "";
    var possible = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@@#$%^&*()_+-=";

    for (var i = 0; i < count; i++)
        text += possible.charAt(Math.floor(Math.random() * possible.length));

    return text;
}