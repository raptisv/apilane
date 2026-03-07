(function () {
    'use strict';

    var TIME_WINDOW_LABELS = { 0: '', 1: '/sec', 2: '/min', 3: '/hr' };
    var RECORD_LABELS = { 0: 'All records', 1: 'Owned only' };
    var ALL_ACTIONS = ['get', 'post', 'put', 'delete'];

    function isDarkTheme() {
        return document.documentElement.getAttribute('data-bs-theme') === 'dark';
    }

    function getColors() {
        var dark = isDarkTheme();
        return {
            fullAccessBg: dark ? '#1a4028' : '#d4edda',
            fullAccessFg: dark ? '#7fcc96' : '#155724',
            restrictedBg: dark ? '#4a3000' : '#fff3cd',
            restrictedFg: dark ? '#ffb347' : '#856404',
            headerBgEven: dark ? '#1e293b' : '#e8ecf1',
            headerBgOdd: dark ? '#1a2332' : '#f0f4f8',
            stickyBg: dark ? '#212529' : '#ffffff'
        };
    }

    function findRule(security, entityName, typeID, roleId, action) {
        for (var i = 0; i < security.length; i++) {
            var s = security[i];
            if (s.Name === entityName && s.TypeID === typeID && s.RoleID === roleId && s.Action === action) {
                return s;
            }
        }
        return null;
    }

    function isActionAllowed(entity, action) {
        switch (action) {
            case 'post': return !!entity.AllowPost;
            case 'put': return !!entity.AllowPut;
            case 'delete': return !!entity.AllowDelete;
            default: return true; // GET is always available
        }
    }

    function isAllProps(properties, configurableProps) {
        if (!properties || properties.trim() === '') return true;
        if (!configurableProps || configurableProps.length === 0) return true;
        var selected = properties.split(',').map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
        if (selected.length < configurableProps.length) return false;
        for (var i = 0; i < configurableProps.length; i++) {
            if (selected.indexOf(configurableProps[i]) === -1) return false;
        }
        return true;
    }

    function splitProps(properties, configurableProps) {
        var selected = [];
        if (properties && properties.trim() !== '') {
            selected = properties.split(',').map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
        }
        var included = [];
        var excluded = [];
        for (var i = 0; i < configurableProps.length; i++) {
            if (selected.indexOf(configurableProps[i]) !== -1) {
                included.push(configurableProps[i]);
            } else {
                excluded.push(configurableProps[i]);
            }
        }
        return { included: included, excluded: excluded };
    }

    function buildTooltipHtml(rule, action, entity) {
        var cfgProps = (action === 'get') ? (entity && entity.PropsGet) : (entity && entity.PropsPostPut);
        var allRecords = rule.Record === 0;
        var allProps = (action === 'delete') || isAllProps(rule.Properties, cfgProps);
        var isFullAccess = allRecords && allProps;

        if (isFullAccess) return '';

        var parts = [];
        var record = RECORD_LABELS[rule.Record] || 'All records';
        parts.push('<strong>Record:</strong> ' + record);

        if (action !== 'delete' && !allProps && cfgProps) {
            var split = splitProps(rule.Properties, cfgProps);
            if (split.included.length > 0) parts.push('<strong>Included:</strong> ' + split.included.join(', '));
            if (split.excluded.length > 0) parts.push('<strong>Excluded:</strong> ' + split.excluded.join(', '));
        }

        return parts.join('<br>');
    }

    // Compute per-entity which actions from the requested list are enabled
    function getEntityColumns(filteredItems, actions) {
        return filteredItems.map(function (entity) {
            var enabled = actions.filter(function (a) { return isActionAllowed(entity, a); });
            return { entity: entity, actions: enabled };
        }).filter(function (col) { return col.actions.length > 0; });
    }

    function renderMatrix(container, filteredItems, security, roles, actions, rateOnly) {
        var entityCols = getEntityColumns(filteredItems, actions);

        if (entityCols.length === 0) {
            var noData = document.createElement('p');
            noData.className = 'text-muted fst-italic';
            noData.textContent = 'No items configured for this category.';
            container.appendChild(noData);
            return;
        }

        var colors = getColors();

        // Build wrapper for horizontal scroll
        var wrapper = document.createElement('div');
        wrapper.style.overflowX = 'auto';

        var table = document.createElement('table');
        table.className = 'table table-bordered table-sm mb-2';
        table.style.whiteSpace = 'nowrap';

        // ---- THEAD ----
        var thead = document.createElement('thead');

        // Row 1: Entity group headers
        var tr1 = document.createElement('tr');
        var thCorner = document.createElement('th');
        thCorner.rowSpan = 2;
        thCorner.style.position = 'sticky';
        thCorner.style.left = '0';
        thCorner.style.zIndex = '3';
        thCorner.style.backgroundColor = colors.stickyBg;
        thCorner.style.verticalAlign = 'middle';
        thCorner.textContent = 'Role';
        tr1.appendChild(thCorner);

        for (var ei = 0; ei < entityCols.length; ei++) {
            var col = entityCols[ei];
            var thEntity = document.createElement('th');
            thEntity.colSpan = col.actions.length;
            thEntity.style.textAlign = 'center';
            thEntity.style.fontWeight = 'bold';
            thEntity.style.backgroundColor = (ei % 2 === 0) ? colors.headerBgEven : colors.headerBgOdd;
            thEntity.textContent = col.entity.Name;
            tr1.appendChild(thEntity);
        }
        thead.appendChild(tr1);

        // Row 2: Action sub-headers (only enabled actions per entity)
        var tr2 = document.createElement('tr');
        for (var ei2 = 0; ei2 < entityCols.length; ei2++) {
            var col2 = entityCols[ei2];
            for (var ai = 0; ai < col2.actions.length; ai++) {
                var thAction = document.createElement('th');
                thAction.style.textAlign = 'center';
                thAction.style.fontSize = '0.7rem';
                thAction.style.textTransform = 'uppercase';
                thAction.style.fontWeight = '600';
                thAction.style.minWidth = '50px';
                thAction.style.backgroundColor = (ei2 % 2 === 0) ? colors.headerBgEven : colors.headerBgOdd;
                thAction.textContent = col2.actions[ai];
                tr2.appendChild(thAction);
            }
        }
        thead.appendChild(tr2);
        table.appendChild(thead);

        // ---- TBODY ----
        var tbody = document.createElement('tbody');

        for (var ri = 0; ri < roles.length; ri++) {
            var role = roles[ri];
            var tr = document.createElement('tr');

            // Role name cell (sticky left)
            var tdRole = document.createElement('td');
            tdRole.style.fontWeight = 'bold';
            tdRole.style.position = 'sticky';
            tdRole.style.left = '0';
            tdRole.style.zIndex = '1';
            tdRole.style.backgroundColor = colors.stickyBg;
            tdRole.textContent = role.displayName || role.roleId;
            tr.appendChild(tdRole);

            for (var ei3 = 0; ei3 < entityCols.length; ei3++) {
                var col3 = entityCols[ei3];
                var ent = col3.entity;

                for (var ai2 = 0; ai2 < col3.actions.length; ai2++) {
                    var action = col3.actions[ai2];
                    var td = document.createElement('td');
                    td.style.textAlign = 'center';
                    td.style.minWidth = '50px';
                    td.style.cursor = 'default';

                    var rule = findRule(security, ent.Name, ent.TypeID, role.roleId, action);
                    if (rule) {
                        // Determine if full access (all records + all props) or restricted
                        var allRecords = rule.Record === 0;
                        var cfgProps = (action === 'get') ? (ent.PropsGet) : (ent.PropsPostPut);
                        var allProps = isAllProps(rule.Properties, cfgProps);
                        var isFullAccess = action === 'delete' ? allRecords : (allRecords && allProps);

                        // Build cell text: checkmark + optional rate limit
                        var cellText = '\u2713';
                        if (rule.RateMax && rule.RateMax > 0) {
                            var tw = TIME_WINDOW_LABELS[rule.RateTw] || '';
                            cellText += ' ' + rule.RateMax + tw;
                        }
                        td.textContent = cellText;

                        td.style.backgroundColor = isFullAccess ? colors.fullAccessBg : colors.restrictedBg;
                        td.style.color = isFullAccess ? colors.fullAccessFg : colors.restrictedFg;
                        td.style.fontWeight = 'bold';
                        if (!rateOnly) {
                            var tooltipContent = buildTooltipHtml(rule, action, ent);
                            if (tooltipContent) {
                                td.setAttribute('data-bs-toggle', 'tooltip');
                                td.setAttribute('data-bs-html', 'true');
                                td.setAttribute('data-bs-placement', 'top');
                                td.setAttribute('title', tooltipContent);
                            }
                        }
                    }
                    // else: not configured — leave blank

                    tr.appendChild(td);
                }
            }

            tbody.appendChild(tr);
        }

        table.appendChild(tbody);
        wrapper.appendChild(table);
        container.appendChild(wrapper);

        // Initialize Bootstrap tooltips for this table
        var tooltipEls = table.querySelectorAll('[data-bs-toggle="tooltip"]');
        for (var ti = 0; ti < tooltipEls.length; ti++) {
            new bootstrap.Tooltip(tooltipEls[ti]);
        }
    }

    // ---- BIND BUTTON ----
    function onButtonClick() {
        var modalEl = document.getElementById('securityMatrixModal');
        if (!modalEl) { return; }

        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();

        var container = document.getElementById('securityMatrixContainer');
        if (!container) { return; }

        // Parse data
        var dataEl = document.getElementById('security-diagram-data');
        if (!dataEl) {
            container.innerHTML = '<p class="text-muted">No security data found.</p>';
            return;
        }

        var data;
        try {
            data = JSON.parse(dataEl.textContent);
        } catch (e) {
            container.innerHTML = '<p class="text-danger">Failed to parse security data.</p>';
            return;
        }

        var items = data.items || [];
        var security = data.security || [];
        var roles = data.roles || [];

        if (items.length === 0 || roles.length === 0) {
            container.innerHTML = '<p class="text-muted">No entities or roles configured.</p>';
            return;
        }

        // Split items by TypeID
        var entitiesItems = items.filter(function (i) { return i.TypeID === 0; });
        var otherItems = items.filter(function (i) { return i.TypeID === 1 || i.TypeID === 2; });

        // Clear container
        container.innerHTML = '';

        // ---- Entities section (all enabled actions, full tooltip) ----
        var entitiesHeading = document.createElement('h5');
        entitiesHeading.className = 'mt-3 mb-2';
        entitiesHeading.innerHTML = '<i class="bi bi-table me-2"></i>Entities';
        container.appendChild(entitiesHeading);

        var entitiesDiv = document.createElement('div');
        container.appendChild(entitiesDiv);
        renderMatrix(entitiesDiv, entitiesItems, security, roles, ALL_ACTIONS, false);

        // ---- Separator ----
        var separator = document.createElement('hr');
        container.appendChild(separator);

        // ---- Schema & Custom Endpoints section (GET only, rate-only tooltip) ----
        var otherHeading = document.createElement('h5');
        otherHeading.className = 'mt-3 mb-2';
        otherHeading.innerHTML = '<i class="bi bi-gear me-2"></i>Schema &amp; Custom Endpoints';
        container.appendChild(otherHeading);

        var otherDiv = document.createElement('div');
        container.appendChild(otherDiv);
        renderMatrix(otherDiv, otherItems, security, roles, ['get'], true);

        // ---- Legend (once at the bottom) ----
        var colors = getColors();
        var legend = document.createElement('div');
        legend.style.fontSize = '0.8rem';
        legend.className = 'text-muted mt-3';
        legend.innerHTML =
            '<span style="color:' + colors.fullAccessFg + ';font-weight:bold;">\u2713</span> = Full access' +
            ' &nbsp;\u00b7&nbsp; ' +
            '<span style="color:' + colors.restrictedFg + ';font-weight:bold;">\u2713</span> = Restricted (owned records or limited properties)';
        container.appendChild(legend);
    }

    var btn = document.getElementById('btnShowSecurityMatrix');
    if (btn) {
        btn.addEventListener('click', onButtonClick);
    }
})();
