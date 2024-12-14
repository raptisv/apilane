
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


function loadReport_Grid(response, element, properties, groupBy) {
    var allData = [];
    $.each(response, function (i, value) {
        var record = {
            label: getLabelForGroup(groupBy, value, false)
        };

        $.each(properties, function (p, property) {
            record[property.Raw] = value[property.Raw.replace('.', '_')];
        });

        allData.push(record);
    });

    var htmlHead = '';
    $.each(groupBy, function (i, groupByItem) {
        $.each(groupByItem.Values, function (j, valueItem) {
            htmlHead += '<th>' + valueItem.Name + '</th>';
        });
    });
    $.each(properties, function (p, property) {
        htmlHead += '<th>' + property.Raw.split('.').join(' ') + '</th>';
    });

    var htmlBody = '';
    $.each(allData, function (i, dataItem) {
        htmlBody += '<tr>';
        $.each(dataItem.label, function (j, label) {
            htmlBody += '<td style="font-weight:bold;">' + label[0] + '</td>';
        });
        $.each(properties, function (p, property) {
            htmlBody += '<td>' + dataItem[property.Raw] + '</td>';
        });
        htmlBody += '</tr>';
    });

    $('#' + element).html('<div class="table-responsive" style="height: 100%;"><table class="table table-sm table-hover table-report-item"><thead><tr>' + htmlHead + '</tr></thead><tbody>' + htmlBody + '</tbody></table></div>');
}

function loadReport_Pie(response, element, properties, groupBy, showLegend) {
    var labels = [];
    var values = [];
    $.each(response, function (i, value) {
        var recordValue = value[properties[0].Raw.replace('.', '_')];
        labels.push(getLabelForGroup(groupBy, value, true).map(function (elem) { return elem[0]; }).join(', '));
        values.push(recordValue);
    });

    $('#' + element).html('<canvas id="' + element + '_chart"></canvas>');

    new Chart(document.getElementById(element + '_chart').getContext('2d'), {
        type: 'pie',
        data: {
            datasets: [{
                data: values,
                fill: false
            }],
            labels: labels
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            legend: {
                display: showLegend,
                position: 'right'
            },
            plugins: {
                colorschemes: {
                    scheme: 'office.Breeze6'
                }
            }
        }
    });
}

// Line charts allows only dates for grouping
function loadReport_Line(response, element, properties, groupBy, showLegend) {
    var allData = [];
    $.each(response, function (i, value) {
        var record = {
            label: getLabelForGroup(groupBy, value)
        };

        $.each(properties, function (p, property) {
            record[property.Raw] = value[property.Raw.replace('.', '_')];
        });

        allData.push(record);
    });

    // Sort by date property
    allData.sort(function (a, b) {
        if ((a.label[0][1] !== null ? a.label[0][1].getTime() : a.label[0][0]) > (b.label[0][1] !== null ? b.label[0][1].getTime() : b.label[0][0]))
            return 1;
        else
            return -1;
    });

    var labels = allData.map(function (elem) { return elem.label[0][0]; });

    var datasets = [];
    $.each(properties, function (p, property) {
        var dataset = [];
        $.each(allData, function (i, item) {
            dataset.push(item[property.Raw]);
        });
        datasets.push({
            label: property.Raw.split('.').join(' '),
            data: dataset,
            fill: false
        });
    });

    $('#' + element).html('<canvas id="' + element + '_chart"></canvas>');

    new Chart(document.getElementById(element + '_chart').getContext('2d'), {
        type: 'line',
        data: {
            labels: labels,
            datasets: datasets
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            legend: {
                display: showLegend,
                position: 'bottom'
            },
            plugins: {
                colorschemes: {
                    scheme: 'office.Breeze6'
                }
            },
            tooltips: {
                intersect: false,
                mode: 'index'
            }
        }
    });
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

function loadApplicationReport(
    reportID,
    appToken,
    portalUserAuthToken,
    type,
    urlStats,
    propertiesForChart,
    groupByForChart) {

    $('#view-api-endpoint-' + reportID).click(function (e) {
        e.preventDefault();

        var url = `${urlStats}&AppToken=${appToken}`;

        Swal.fire({
            title: "API endpoint",
            html: `<textarea readonly rows="10" class="w-100 form-control">${url}</textarea>`,
            showCancelButton: true,
            showConfirmButton: true,
            cancelButtonText: 'Close',
            confirmButtonText: "Open in a new tab",
            buttonsStyling: false,
            customClass: {
                confirmButton: 'btn btn-primary',
                cancelButton: 'btn btn-secondary ms-2'
            }
        }).then((submit) => {
            if (submit.isConfirmed) {
                window.open(url, '_blank');
            }
        });
    });

    setupResizeLinks(reportID, appToken, 4);
    setupResizeLinks(reportID, appToken, 6);
    setupResizeLinks(reportID, appToken, 12);

    $('#refresh-report-' + reportID).click(function (e) {
        loadApplicationReportChart(reportID, appToken, portalUserAuthToken, type, urlStats, propertiesForChart, groupByForChart);
    });

    loadApplicationReportChart(reportID, appToken, portalUserAuthToken, type, urlStats, propertiesForChart, groupByForChart);    
}

function setupResizeLinks(reportID, appToken, col) {
    $('#resize-' + col + '-' + reportID).click(function (e) {
        $.ajax({
            url: '/App/' + appToken + '/Reports/SetWidth?ID=' + reportID + '&Width=' + col,
            type: 'GET',
            success: function (data) {
                $('.report-' + reportID).attr('class', 'report-' + reportID + ' col-lg-' + col + ' col-md-' + col + ' col-sm-12 col-xs-12 mt-4');
                $('.resize-link-' + reportID).removeClass('disabled');
                $('#resize-' + col + '-' + reportID).addClass('disabled');
            },
            error: function (e) {
                // Do nothing?
            }
        });
    });
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

function loadApplicationReportChart(reportID, appToken, portalUserAuthToken, type, urlStats, propertiesForChart, groupByForChart) {

    $('#report_' + reportID).html(`<div class="text-center">
                                    <div class="spinner-border">
                                        <span class="visually-hidden">Loading...</span>
                                    </div>
                                </div>`);

    apiCall(urlStats, appToken, portalUserAuthToken, function (response) {
        $('#report_' + reportID).empty();
        if (response.length === 0) {
            $('#report_' + reportID).html('<div class="text-center"><span class="text-muted" style="position: absolute; top: 50%;">No data</span></div>');
            return;
        }

        try {
            if (type === 'Pie') {
                loadReport_Pie(response, 'report_' + reportID, propertiesForChart, groupByForChart, true);
            } else if (type === 'Line') {
                loadReport_Line(response, 'report_' + reportID, propertiesForChart, groupByForChart, true);
            } else if (type === 'Grid') {
                loadReport_Grid(response, 'report_' + reportID, propertiesForChart, groupByForChart, true);
            }
        } catch (err) {
            console.error(err);
            $('#report_' + reportID).html('<div style="text-align: center;"><span class="label label-danger">' + err + '</span><div>');
        }
    },
        function (e) {
            var jsonResponse = { Message: 'Error' };
            if (e.responseJSON) {
                jsonResponse = e.responseJSON;
            }
            else if (e.xhr && e.xhr.responseText) {
                jsonResponse = JSON.parse(e.xhr.responseText);
            }
            $('#report_' + reportID).html('<div style="text-align: center;"><span class="label label-danger">' + jsonResponse.Message + '</span><div>');
        });
}

function loadApplicationDisplayToken(appId, appToken, appEncryptionKey) {
    $('.swal-display-token-' + appId).click(function () {
        Swal.fire({
            title: "Application info",
            html: `<div class="alert alert-primary fade show">Token: <b>${appToken}</b></div><div class="alert alert-primary fade show">Encryption key: <b>${appEncryptionKey}</b></div>`,
            showCancelButton: false,
            showConfirmButton: true,
            confirmButtonText: "OK",
            allowOutsideClick: true,
            buttonsStyling: false,
            customClass: {
                confirmButton: 'btn btn-primary'
            },
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