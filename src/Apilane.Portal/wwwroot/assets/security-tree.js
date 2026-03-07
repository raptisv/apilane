(function () {
    'use strict';

    var TIME_WINDOW_LABELS = { 0: '', 1: '/sec', 2: '/min', 3: '/hr' };
    var ROLE_COLORS = d3.schemeCategory10;
    var TRANSITION_DURATION = 500;
    var NODE_VERTICAL_SPACING = 25;
    var NODE_HORIZONTAL_SPACING = 250;
    var ZOOM_EXTENT = [0.1, 5];
    var FONT_SIZE = 13;

    function isDarkTheme() {
        return document.documentElement.getAttribute('data-bs-theme') === 'dark';
    }

    function getThemeColors() {
        var dark = isDarkTheme();
        return {
            text: dark ? '#e0e0e0' : '#222',
            link: dark ? '#555' : '#ccc',
            background: dark ? '#1e1e1e' : '#fff',
            collapsedStroke: dark ? '#aaa' : '#666',
            legendBg: dark ? 'rgba(30,30,30,0.85)' : 'rgba(255,255,255,0.85)',
            legendBorder: dark ? '#555' : '#ccc',
            legendText: dark ? '#e0e0e0' : '#333'
        };
    }

    function parseSecurityData() {
        var el = document.getElementById('security-diagram-data');
        if (!el) return null;
        try {
            return JSON.parse(el.textContent);
        } catch (e) {
            console.error('security-tree: failed to parse data', e);
            return null;
        }
    }

    function recordLabel(record) {
        return record === 1 ? 'Owned records' : 'All records';
    }

    function rateLabel(rateMax, rateTw) {
        if (!rateMax) return '';
        var tw = TIME_WINDOW_LABELS[rateTw] || '';
        return rateMax + tw;
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

    // Full label for entity endpoints
    function buildEndpointLabel(action, rule, entityItem) {
        var cfgProps = (action === 'get') ? (entityItem && entityItem.PropsGet) : (entityItem && entityItem.PropsPostPut);
        var allRecords = rule.Record === 0;
        var allProps = (action === 'delete') || isAllProps(rule.Properties, cfgProps);
        var isFullAccess = allRecords && allProps;

        var parts = [action.toUpperCase()];

        if (!isFullAccess) {
            if (!allRecords) parts.push('\u00b7 Owned records');
            if (action !== 'delete' && !allProps && cfgProps) {
                var split = splitProps(rule.Properties, cfgProps);
                if (split.included.length > 0) parts.push('\u00b7 Included: ' + split.included.join(', '));
                if (split.excluded.length > 0) parts.push('\u00b7 Excluded: ' + split.excluded.join(', '));
            }
        }

        var rate = rateLabel(rule.RateMax, rule.RateTw);
        if (rate) parts.push('\u00b7 ' + rate);

        return parts.join(' ');
    }

    // Simple label for schema/custom endpoints (action + rate only)
    function buildEndpointLabelSimple(action, rule) {
        var parts = [action.toUpperCase()];
        var rate = rateLabel(rule.RateMax, rule.RateTw);
        if (rate) {
            parts.push('· ' + rate);
        }
        return parts.join(' ');
    }

    function buildRoleColorMap(roles) {
        var map = {};
        roles.forEach(function (r, i) {
            map[r.roleId] = ROLE_COLORS[i % ROLE_COLORS.length];
        });
        return map;
    }

    function buildEntityNodes(itemsList, roleRules, roleColor, labelFn) {
        var nodes = [];

        // Collect entity names that have rules
        var entityNames = [];
        roleRules.forEach(function (rule) {
            var inList = itemsList.some(function (it) { return it.Name === rule.Name; });
            if (inList && entityNames.indexOf(rule.Name) === -1) {
                entityNames.push(rule.Name);
            }
        });

        entityNames.forEach(function (entityName) {
            var entityItem = itemsList.find(function (it) { return it.Name === entityName; });
            var entityRules = roleRules.filter(function (r) { return r.Name === entityName; });

            var entityNode = {
                name: entityName,
                nodeType: 'entity',
                color: roleColor,
                children: []
            };

            var actions = ['get', 'post', 'put', 'delete'];
            actions.forEach(function (action) {
                var rule = entityRules.find(function (r) { return r.Action === action; });
                if (!rule) {
                    if (entityItem) {
                        var allowed = false;
                        if (action === 'post') allowed = entityItem.AllowPost;
                        if (action === 'put') allowed = entityItem.AllowPut;
                        if (action === 'delete') allowed = entityItem.AllowDelete;
                        if (action === 'get') allowed = true;
                        if (!allowed) return;
                    }
                    return;
                }
                var cfgProps = (action === 'get') ? (entityItem && entityItem.PropsGet) : (entityItem && entityItem.PropsPostPut);
                var allRec = rule.Record === 0;
                var allPrp = (action === 'delete') || isAllProps(rule.Properties, cfgProps);
                entityNode.children.push({
                    name: labelFn(action, rule, entityItem),
                    nodeType: 'endpoint',
                    action: action,
                    color: roleColor,
                    hasRule: true,
                    isFullAccess: allRec && allPrp
                });
            });

            if (entityNode.children.length > 0) {
                nodes.push(entityNode);
            }
        });

        return nodes;
    }

    function buildTreeData(data) {
        var items = data.items || [];
        var security = data.security || [];
        var roles = data.roles || [];
        var roleColorMap = buildRoleColorMap(roles);

        var entitiesItems = items.filter(function (i) { return i.TypeID === 0; });
        var otherItems = items.filter(function (i) { return i.TypeID === 1 || i.TypeID === 2; });

        var root = {
            name: 'Security',
            nodeType: 'root',
            children: []
        };

        roles.forEach(function (role) {
            var roleColor = roleColorMap[role.roleId];

            var roleRules = security.filter(function (s) {
                return s.RoleID === role.roleId;
            });

            var roleNode = {
                name: role.displayName,
                nodeType: 'role',
                roleId: role.roleId,
                color: roleColor,
                children: []
            };

            // --- Entities category ---
            var entityNodes = buildEntityNodes(entitiesItems, roleRules, roleColor, buildEndpointLabel);
            if (entityNodes.length > 0) {
                roleNode.children.push({
                    name: 'Entities',
                    nodeType: 'category',
                    color: roleColor,
                    children: entityNodes
                });
            }

            // --- Schema & Custom Endpoints category ---
            var otherNodes = buildEntityNodes(otherItems, roleRules, roleColor, buildEndpointLabelSimple);
            if (otherNodes.length > 0) {
                roleNode.children.push({
                    name: 'Schema & Custom Endpoints',
                    nodeType: 'category',
                    color: roleColor,
                    children: otherNodes
                });
            }

            if (roleNode.children.length > 0) {
                root.children.push(roleNode);
            }
        });

        return root;
    }

    function collapseEntities(node) {
        if (node.children) {
            node.children.forEach(function (child) {
                if (child.data && (child.data.nodeType === 'entity' || child.data.nodeType === 'category')) {
                    if (child.children) {
                        child._children = child.children;
                        child.children = null;
                    }
                } else if (child.children) {
                    collapseEntities(child);
                }
            });
        }
    }

    function renderTree(container) {
        var theme = getThemeColors();
        var data = parseSecurityData();
        if (!data) return;

        var treeData = buildTreeData(data);
        var roles = data.roles || [];
        var roleColorMap = buildRoleColorMap(roles);

        if (!treeData.children || treeData.children.length === 0) {
            container.innerHTML = '<p class="text-muted ms-3 mt-2">No security rules configured.</p>';
            return;
        }

        container.innerHTML = '';

        var width = container.clientWidth || 960;
        var height = container.clientHeight || 600;

        var svg = d3.select(container)
            .append('svg')
            .attr('width', '100%')
            .attr('height', '100%')
            .attr('viewBox', '0 0 ' + width + ' ' + height)
            .style('font', FONT_SIZE + 'px sans-serif');

        var g = svg.append('g');

        var zoomBehavior = d3.zoom()
            .scaleExtent(ZOOM_EXTENT)
            .on('zoom', function (event) {
                g.attr('transform', event.transform);
            });

        svg.call(zoomBehavior);

        var hierarchyRoot = d3.hierarchy(treeData);

        collapseEntities(hierarchyRoot);

        var treeLayout = d3.tree().nodeSize([NODE_VERTICAL_SPACING, NODE_HORIZONTAL_SPACING]);

        var linkGroup = g.append('g').attr('class', 'links');
        var nodeGroup = g.append('g').attr('class', 'nodes');

        function update(source) {
            treeLayout(hierarchyRoot);

            var nodes = hierarchyRoot.descendants();
            var links = hierarchyRoot.links();

            // Links
            var link = linkGroup.selectAll('path.tree-link')
                .data(links, function (d) {
                    return (d.source.data.name || '') + '->' + (d.target.data.name || '') + '-' + d.target.depth;
                });

            var linkEnter = link.enter()
                .append('path')
                .attr('class', 'tree-link')
                .attr('fill', 'none')
                .attr('stroke', theme.link)
                .attr('stroke-width', 1.5)
                .attr('d', function () {
                    var o = { x: source.x0 || source.x, y: source.y0 || source.y };
                    return d3.linkHorizontal()
                        .x(function (d) { return d.y; })
                        .y(function (d) { return d.x; })
                        ({ source: o, target: o });
                });

            var linkMerge = linkEnter.merge(link);

            linkMerge.transition()
                .duration(TRANSITION_DURATION)
                .attr('d', d3.linkHorizontal()
                    .x(function (d) { return d.y; })
                    .y(function (d) { return d.x; })
                )
                .attr('stroke', theme.link);

            link.exit()
                .transition()
                .duration(TRANSITION_DURATION)
                .attr('d', function () {
                    var o = { x: source.x, y: source.y };
                    return d3.linkHorizontal()
                        .x(function (d) { return d.y; })
                        .y(function (d) { return d.x; })
                        ({ source: o, target: o });
                })
                .remove();

            // Nodes
            var node = nodeGroup.selectAll('g.tree-node')
                .data(nodes, function (d) {
                    return d.data.name + '-' + d.depth + '-' + (d.parent ? d.parent.data.name : 'root');
                });

            var nodeEnter = node.enter()
                .append('g')
                .attr('class', 'tree-node')
                .attr('transform', function () {
                    var x0 = source.x0 !== undefined ? source.x0 : source.x;
                    var y0 = source.y0 !== undefined ? source.y0 : source.y;
                    return 'translate(' + y0 + ',' + x0 + ')';
                })
                .style('cursor', function (d) {
                    return d.data.nodeType === 'endpoint' ? 'default' : 'pointer';
                })
                .on('click', function (event, d) {
                    if (d.data.nodeType === 'endpoint') return;
                    if (d.children) {
                        d._children = d.children;
                        d.children = null;
                    } else if (d._children) {
                        d.children = d._children;
                        d._children = null;
                    }
                    update(d);
                });

            // Draw node shapes
            nodeEnter.each(function (d) {
                var el = d3.select(this);
                var nt = d.data.nodeType;

                if (nt === 'root') {
                    el.append('circle')
                        .attr('r', 8)
                        .attr('fill', theme.text)
                        .attr('stroke', theme.text)
                        .attr('stroke-width', 2);
                } else if (nt === 'role') {
                    el.append('circle')
                        .attr('r', 7)
                        .attr('fill', d.data.color)
                        .attr('stroke', d.data.color)
                        .attr('stroke-width', 2);
                } else if (nt === 'category') {
                    el.append('rect')
                        .attr('x', -6)
                        .attr('y', -6)
                        .attr('width', 12)
                        .attr('height', 12)
                        .attr('rx', 3)
                        .attr('fill', d.data.color || '#888')
                        .attr('stroke', d.data.color || '#888')
                        .attr('stroke-width', 1.5);
                } else if (nt === 'entity') {
                    el.append('rect')
                        .attr('x', -5)
                        .attr('y', -5)
                        .attr('width', 10)
                        .attr('height', 10)
                        .attr('transform', 'rotate(45)')
                        .attr('fill', d.data.color || '#888')
                        .attr('stroke', d.data.color || '#888')
                        .attr('stroke-width', 1.5);
                } else if (nt === 'endpoint') {
                    el.append('circle')
                        .attr('r', 4)
                        .attr('fill', d.data.isFullAccess ? '#4caf50' : '#ff9800')
                        .attr('stroke', d.data.isFullAccess ? '#388e3c' : '#e65100')
                        .attr('stroke-width', 1.5);
                }
            });

            // Labels
            nodeEnter.append('text')
                .attr('dy', '0.35em')
                .attr('x', function (d) {
                    if (d.data.nodeType === 'endpoint') return 10;
                    return 14;
                })
                .attr('fill', theme.text)
                .attr('font-size', FONT_SIZE + 'px')
                .text(function (d) { return d.data.name; });

            // Merge
            var nodeMerge = nodeEnter.merge(node);

            nodeMerge.transition()
                .duration(TRANSITION_DURATION)
                .attr('transform', function (d) {
                    return 'translate(' + d.y + ',' + d.x + ')';
                });

            // Update collapsed styling
            nodeMerge.each(function (d) {
                var el = d3.select(this);
                var nt = d.data.nodeType;

                if (nt === 'role') {
                    el.select('circle')
                        .attr('fill', d._children ? 'none' : d.data.color)
                        .attr('stroke', d.data.color)
                        .attr('stroke-dasharray', d._children ? '3,2' : null);
                } else if (nt === 'category') {
                    el.select('rect')
                        .attr('fill', d._children ? 'none' : (d.data.color || '#888'))
                        .attr('stroke', d.data.color || '#888')
                        .attr('stroke-dasharray', d._children ? '3,2' : null);
                } else if (nt === 'entity') {
                    el.select('rect')
                        .attr('fill', d._children ? 'none' : (d.data.color || '#888'))
                        .attr('stroke', d.data.color || '#888')
                        .attr('stroke-dasharray', d._children ? '3,2' : null);
                } else if (nt === 'root') {
                    el.select('circle')
                        .attr('fill', d._children ? 'none' : theme.text)
                        .attr('stroke', theme.text)
                        .attr('stroke-dasharray', d._children ? '3,2' : null);
                }
            });

            node.exit()
                .transition()
                .duration(TRANSITION_DURATION)
                .attr('transform', function () {
                    return 'translate(' + source.y + ',' + source.x + ')';
                })
                .remove();

            // Store positions for transitions
            nodes.forEach(function (d) {
                d.x0 = d.x;
                d.y0 = d.y;
            });
        }

        hierarchyRoot.x0 = 0;
        hierarchyRoot.y0 = 0;
        update(hierarchyRoot);

        // Center the tree initially
        var allNodes = hierarchyRoot.descendants();
        var minX = d3.min(allNodes, function (d) { return d.x; }) || 0;
        var maxX = d3.max(allNodes, function (d) { return d.x; }) || 0;
        var initialY = 80;
        var initialX = height / 2 - (minX + maxX) / 2;
        svg.call(
            zoomBehavior.transform,
            d3.zoomIdentity.translate(initialY, initialX)
        );

        // Legend
        var legendData = roles
            .filter(function (r) {
                return roleColorMap[r.roleId] !== undefined;
            })
            .map(function (r) {
                return { name: r.displayName, color: roleColorMap[r.roleId] };
            });

        if (legendData.length > 0) {
            var legend = svg.append('g')
                .attr('class', 'tree-legend')
                .attr('transform', 'translate(' + (width - 200) + ', 20)');

            legend.append('rect')
                .attr('x', -10)
                .attr('y', -10)
                .attr('width', 190)
                .attr('height', legendData.length * 22 + 20)
                .attr('rx', 6)
                .attr('fill', theme.legendBg)
                .attr('stroke', theme.legendBorder)
                .attr('stroke-width', 1);

            legendData.forEach(function (item, i) {
                var row = legend.append('g')
                    .attr('transform', 'translate(0,' + (i * 22) + ')');

                row.append('circle')
                    .attr('r', 5)
                    .attr('cx', 5)
                    .attr('cy', 6)
                    .attr('fill', item.color);

                row.append('text')
                    .attr('x', 16)
                    .attr('y', 10)
                    .attr('fill', theme.legendText)
                    .attr('font-size', '12px')
                    .text(item.name);
            });
        }
    }

    var btn = document.getElementById('btnShowSecurityTree');
    if (btn) {
        btn.addEventListener('click', function () {
            var modalEl = document.getElementById('securityTreeModal');
            if (!modalEl) return;

            var modal = new bootstrap.Modal(modalEl);
            modal.show();

            var container = document.getElementById('securityTreeContainer');
            if (!container) return;

            // Clear and re-render when modal is shown
            modalEl.addEventListener('shown.bs.modal', function onShown() {
                modalEl.removeEventListener('shown.bs.modal', onShown);
                renderTree(container);
            });

            // If modal is already visible (edge case), render immediately
            if (modalEl.classList.contains('show')) {
                renderTree(container);
            }
        });
    }
})();
