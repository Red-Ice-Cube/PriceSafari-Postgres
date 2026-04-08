



(function () {
    'use strict';
    function MassActions(config) {

        const storeId = config.storeId;
        const isAllegro = config.isAllegro || false;
        const storageKey = config.storageKey;
        const flags = config.flags || [];

        // ═══ NOWE: Dodatkowe akcje dynamiczne ═══
        const extraActions = config.extraActions || [];

        const getAllPrices = config.getAllPrices;
        const getFilteredPrices = config.getFilteredPrices;
        const getProductIdentifier = config.getProductIdentifier;

        const showLoading = config.showLoading;
        const hideLoading = config.hideLoading;
        const showGlobalUpdate = config.showGlobalUpdate;
        const showGlobalNotification = config.showGlobalNotification;
        const onFlagsUpdated = config.onFlagsUpdated;
        const onAutomationsUpdated = config.onAutomationsUpdated;
        const onSelectionChanged = config.onSelectionChanged || function () { };

        let selectedProductIds = _loadFromStorage();
        let _pendingRefreshCallbacks = [];

        function _loadFromStorage() {
            const stored = localStorage.getItem(storageKey);
            return stored ? new Set(JSON.parse(stored)) : new Set();
        }

        function _saveToStorage() {
            localStorage.setItem(storageKey, JSON.stringify(Array.from(selectedProductIds)));
        }

        function _clearStorage() {
            localStorage.removeItem(storageKey);
        }

        function isSelected(id) {
            return selectedProductIds.has(id.toString());
        }

        function getSelected() {
            return selectedProductIds;
        }

        function getCount() {
            return selectedProductIds.size;
        }

        function addProduct(id) {
            selectedProductIds.add(id.toString());
            _saveToStorage();
            updateCounters();
            onSelectionChanged(selectedProductIds);
        }

        function removeProduct(id) {
            selectedProductIds.delete(id.toString());
            _saveToStorage();
            updateCounters();
            onSelectionChanged(selectedProductIds);
        }

        function toggleProduct(id) {
            const idStr = id.toString();
            if (selectedProductIds.has(idStr)) {
                selectedProductIds.delete(idStr);
            } else {
                selectedProductIds.add(idStr);
            }
            _saveToStorage();
            updateCounters();
            onSelectionChanged(selectedProductIds);
        }

        function selectAllVisible() {
            const filtered = getFilteredPrices();
            if (!filtered || filtered.length === 0) return;
            filtered.forEach(function (product) {
                selectedProductIds.add(product.productId.toString());
            });
            _saveToStorage();
            updateVisibleButtons();
            updateSelectionUI();
            onSelectionChanged(selectedProductIds);
        }

        function deselectAllVisible() {
            const filtered = getFilteredPrices();
            if (!filtered || filtered.length === 0) return;
            filtered.forEach(function (product) {
                selectedProductIds.delete(product.productId.toString());
            });
            _saveToStorage();
            updateVisibleButtons();
            updateSelectionUI();
            onSelectionChanged(selectedProductIds);
        }

        function clearSelection() {
            selectedProductIds.clear();
            _clearStorage();
            updateSelectionUI();
            updateVisibleButtons();
            onSelectionChanged(selectedProductIds);
        }

        function updateVisibleButtons() {
            document.querySelectorAll('.select-product-btn').forEach(function (btn) {
                var productId = btn.dataset.productId;
                if (selectedProductIds.has(productId)) {
                    btn.textContent = 'Wybrano';
                    btn.classList.add('selected');
                } else {
                    btn.textContent = 'Zaznacz';
                    btn.classList.remove('selected');
                }
            });
        }

        function updateCounters() {
            var counter = document.getElementById('selectedProductsCounter');
            var modalCounter = document.getElementById('selectedProductsModalCounter');
            var count = selectedProductIds.size;

            if (counter) counter.textContent = 'Wybrano: ' + count;
            if (modalCounter) modalCounter.textContent = count;
        }

        function updateSelectionUI() {
            updateCounters();

            var count = selectedProductIds.size;
            var selectionContainer = document.getElementById('selectionContainer');
            if (selectionContainer) selectionContainer.style.display = 'flex';

            var modalList = document.getElementById('selectedProductsList');
            if (!modalList) return;
            modalList.innerHTML = '';

            if (count === 0) {
                modalList.innerHTML = '<div class="alert alert-info text-center">Nie zaznaczono żadnych produktów.</div>';
                return;
            }

            var allPrices = getAllPrices();

            var table = document.createElement('table');
            table.className = 'table-orders';
            table.innerHTML =
                '<thead><tr>' +
                '<th style="width: 5%; text-align: center;">#</th>' +
                '<th style="width: 70%;">Nazwa Produktu</th>' +
                '<th>ID/EAN</th>' +
                '</tr></thead>';

            var tbody = document.createElement('tbody');
            var index = 0;

            selectedProductIds.forEach(function (productId) {
                var product = allPrices.find(function (p) { return p.productId.toString() === productId; });
                if (!product) return;

                index++;
                var idInfo = getProductIdentifier(product);
                var identifierText = idInfo.value ? (idInfo.label + ' ' + idInfo.value) : ('Brak ' + idInfo.label);

                var tr = document.createElement('tr');
                tr.innerHTML =
                    '<td class="align-middle text-center" style="color: #858796; font-size: 13px;">' + index + '</td>' +
                    '<td class="align-middle">' + product.productName + '</td>' +
                    '<td class="align-middle">' + identifierText + '</td>';
                tbody.appendChild(tr);
            });

            table.appendChild(tbody);
            modalList.appendChild(table);
        }

        function _hexToRgba(hex, alpha) {
            var r = 0, g = 0, b = 0;
            if (hex.length === 4) {
                r = parseInt(hex[1] + hex[1], 16);
                g = parseInt(hex[2] + hex[2], 16);
                b = parseInt(hex[3] + hex[3], 16);
            } else if (hex.length === 7) {
                r = parseInt(hex[1] + hex[2], 16);
                g = parseInt(hex[3] + hex[4], 16);
                b = parseInt(hex[5] + hex[6], 16);
            }
            return 'rgba(' + r + ', ' + g + ', ' + b + ', ' + alpha + ')';
        }

        function _formatPricePL(value) {
            if (value === null || value === undefined || isNaN(parseFloat(value))) return 'N/A';
            return parseFloat(value).toLocaleString('pl-PL', {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }) + ' PLN';
        }

        function _buildFlagGetPayload(productIdsArray) {
            if (isAllegro) {
                return { allegroProductIds: productIdsArray };
            }
            return { productIds: productIdsArray.map(String), isAllegro: false };
        }

        function _buildFlagSavePayload(productIdsArray, flagsToAdd, flagsToRemove) {
            if (isAllegro) {
                return {
                    allegroProductIds: productIdsArray,
                    flagsToAdd: flagsToAdd,
                    flagsToRemove: flagsToRemove
                };
            }
            return {
                productIds: productIdsArray,
                flagsToAdd: flagsToAdd,
                flagsToRemove: flagsToRemove,
                isAllegro: false
            };
        }

        function _askContinueOrFinish(refreshCallback) {
            if (refreshCallback) {
                _pendingRefreshCallbacks.push(refreshCallback);
            }

            $('#continueActionsModal').modal('show');
        }

        function _handleContinueYes() {
            $('#continueActionsModal').modal('hide');
            setTimeout(function () {
                updateSelectionUI();
                $('#selectedProductsModal').modal('show');
            }, 300);
        }

        function _handleContinueNo() {
            $('#continueActionsModal').modal('hide');
            clearSelection();
            _flushPendingRefreshes();
        }

        function _flushPendingRefreshes() {
            if (_pendingRefreshCallbacks.length === 0) return;
            var callbacks = _pendingRefreshCallbacks.slice();
            _pendingRefreshCallbacks = [];
            callbacks.forEach(function (cb) {
                try { cb(); } catch (e) { console.error('Refresh callback error:', e); }
            });
        }

        function _openFlagModal() {
            if (selectedProductIds.size === 0) {
                alert('Nie zaznaczono żadnych produktów.');
                return;
            }

            $('#selectedProductsModal').modal('hide');
            showLoading();

            var productIdsArray = Array.from(selectedProductIds).map(function (id) { return parseInt(id); });

            fetch('/ProductFlags/GetFlagCountsForProducts', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(_buildFlagGetPayload(productIdsArray))
            })
                .then(function (response) { return response.json(); })
                .then(function (counts) {
                    _populateFlagModal(counts);
                    hideLoading();
                    $('#flagModal').modal('show');
                })
                .catch(function (error) {
                    console.error('Błąd pobierania liczników flag:', error);
                    hideLoading();
                    alert('Nie udało się pobrać danych o flagach.');
                    $('#selectedProductsModal').modal('show');
                });
        }

        function _populateFlagModal(flagCounts) {
            var modalBody = document.getElementById('flagModalBody');
            var flagModalTitle = document.querySelector('#flagModal .price-box-column-name');
            var totalSelected = selectedProductIds.size;

            if (flagModalTitle) {
                flagModalTitle.textContent = 'Zarządzaj flagami dla ' + totalSelected + ' produktów';
            }
            modalBody.innerHTML = '';

            flags.forEach(function (flag) {
                var currentCount = flagCounts[flag.flagId] || 0;

                var flagItem = document.createElement('div');
                flagItem.className = 'bulk-flag-item';
                flagItem.dataset.flagId = flag.flagId;

                flagItem.innerHTML =
                    '<div class="flag-label">' +
                    '<span class="flag-name" style="border-color: ' + flag.flagColor + '; color: ' + flag.flagColor + '; background-color: ' + _hexToRgba(flag.flagColor, 0.3) + ';">' + flag.flagName + '</span>' +
                    '<span class="flag-count">(' + currentCount + ' / ' + totalSelected + ')</span>' +
                    '</div>' +
                    '<div class="flag-actions">' +
                    '<div class="action-group">' +
                    '<label><input type="checkbox" class="bulk-flag-action" data-action="add" ' + (currentCount === totalSelected ? 'disabled' : '') + '> Dodaj</label>' +
                    '<span class="change-indicator add-indicator">' + currentCount + ' → ' + totalSelected + '</span>' +
                    '</div>' +
                    '<div class="action-group">' +
                    '<label><input type="checkbox" class="bulk-flag-action" data-action="remove" ' + (currentCount === 0 ? 'disabled' : '') + '> Odepnij</label>' +
                    '<span class="change-indicator remove-indicator">' + currentCount + ' → 0</span>' +
                    '</div>' +
                    '</div>';

                modalBody.appendChild(flagItem);
            });

            modalBody.querySelectorAll('.bulk-flag-action').forEach(function (checkbox) {
                checkbox.addEventListener('change', function () {
                    var parentItem = this.closest('.bulk-flag-item');
                    var action = this.dataset.action;

                    if (this.checked) {
                        if (action === 'add') {
                            parentItem.querySelector('[data-action="remove"]').checked = false;
                        } else {
                            parentItem.querySelector('[data-action="add"]').checked = false;
                        }
                    }

                    parentItem.querySelector('.add-indicator').style.display =
                        parentItem.querySelector('[data-action="add"]').checked ? 'inline' : 'none';
                    parentItem.querySelector('.remove-indicator').style.display =
                        parentItem.querySelector('[data-action="remove"]').checked ? 'inline' : 'none';
                });
            });
        }

        function _saveFlags() {
            var flagsToAdd = [];
            var flagsToRemove = [];

            document.querySelectorAll('#flagModalBody .bulk-flag-item').forEach(function (item) {
                var flagId = parseInt(item.dataset.flagId, 10);
                var addCb = item.querySelector('[data-action="add"]');
                var removeCb = item.querySelector('[data-action="remove"]');
                if (addCb && addCb.checked) flagsToAdd.push(flagId);
                if (removeCb && removeCb.checked) flagsToRemove.push(flagId);
            });

            if (flagsToAdd.length === 0 && flagsToRemove.length === 0) {
                alert('Nie wybrano żadnych akcji do wykonania.');
                return;
            }

            var productIdsArray = Array.from(selectedProductIds).map(function (id) { return parseInt(id); });
            var payload = _buildFlagSavePayload(productIdsArray, flagsToAdd, flagsToRemove);

            showLoading();

            fetch('/ProductFlags/UpdateFlagsForMultipleProducts', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            })
                .then(function (response) { return response.json(); })
                .then(function (response) {
                    if (response.success) {
                        $('#flagModal').modal('hide');
                        _askContinueOrFinish(onFlagsUpdated);
                    } else {
                        alert('Błąd: ' + response.message);
                    }
                })
                .catch(function (error) {
                    console.error('Błąd masowej aktualizacji flag:', error);
                    alert('Wystąpił błąd połączenia.');
                })
                .finally(function () { hideLoading(); });
        }

        function _openAutomationModal() {
            if (selectedProductIds.size === 0) {
                alert('Nie zaznaczono żadnych produktów.');
                return;
            }

            var sourceType = isAllegro ? 1 : 0;
            var productIdsArray = Array.from(selectedProductIds).map(function (id) { return parseInt(id); });

            var countDisplay = document.getElementById('automationProductCountDisplay');
            if (countDisplay) countDisplay.textContent = selectedProductIds.size;

            $('#selectedProductsModal').modal('hide');
            showLoading();

            fetch('/AutomationRules/GetRulesStatusForProducts', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    StoreId: storeId,
                    SourceType: sourceType,
                    IsAllegro: isAllegro,
                    ProductIds: productIdsArray
                })
            })
                .then(function (response) {
                    if (!response.ok) throw new Error('Błąd sieci: ' + response.statusText);
                    return response.json();
                })
                .then(function (rules) {
                    _renderAutomationRules(rules, selectedProductIds.size);
                    hideLoading();
                    $('#automationSelectionModal').modal('show');
                })
                .catch(function (error) {
                    console.error('Błąd pobierania reguł:', error);
                    hideLoading();
                    alert('Błąd komunikacji z serwerem. Sprawdź konsolę.');
                    $('#selectedProductsModal').modal('show');
                });
        }

        function _renderAutomationRules(rules, totalSelected) {
            var container = document.getElementById('automationRulesListContainer');
            if (!container) {
                console.error('BŁĄD: Nie znaleziono kontenera automationRulesListContainer w modalu!');
                return;
            }
            container.innerHTML = '';

            var totalAssignedInSelection = rules ? rules.reduce(function (sum, r) { return sum + r.matchingCount; }, 0) : 0;
            var totalUnassignedInSelection = totalSelected - totalAssignedInSelection;

            var statsHeader = document.createElement('div');
            statsHeader.style.cssText = 'background-color:#f8f9fa; border:1px solid #e3e6f0; border-radius:8px; padding:15px; margin-bottom:20px; display:flex; justify-content:space-around; align-items:center; text-align:center;';
            statsHeader.innerHTML =
                '<div>' +
                '<div style="font-size:11px; color:#858796; text-transform:uppercase; font-weight:700; letter-spacing:0.5px;">Łącznie zaznaczone</div>' +
                '<div style="font-size:20px; font-weight:700; color:#5a5c69;">' + totalSelected + '</div>' +
                '</div>' +
                '<div style="border-left:1px solid #e3e6f0; height:30px;"></div>' +
                '<div>' +
                '<div style="font-size:11px; color:#858796; text-transform:uppercase; font-weight:700; letter-spacing:0.5px;">Przypisane do reguły</div>' +
                '<div style="font-size:20px; font-weight:700; color:#5a5c69;">' + totalAssignedInSelection + '</div>' +
                '</div>' +
                '<div style="border-left:1px solid #e3e6f0; height:30px;"></div>' +
                '<div>' +
                '<div style="font-size:11px; color:#e74a3b; text-transform:uppercase; font-weight:700; letter-spacing:0.5px;">Nieprzypisane</div>' +
                '<div style="font-size:20px; font-weight:700; color:#e74a3b;">' + Math.max(0, totalUnassignedInSelection) + '</div>' +
                '</div>';
            container.appendChild(statsHeader);

            var hasAssignments = totalAssignedInSelection > 0;
            var unassignDiv = document.createElement('div');
            unassignDiv.className = 'automation-rule-item';
            unassignDiv.style.cssText =
                'border:1px dashed ' + (hasAssignments ? '#e74a3b' : '#ccc') + '; ' +
                'border-radius:8px; padding:10px 15px; ' +
                'cursor:' + (hasAssignments ? 'pointer' : 'default') + '; ' +
                'background-color:#fff; display:flex; justify-content:space-between; align-items:center; ' +
                'transition:background-color 0.2s; margin-bottom:20px; opacity:' + (hasAssignments ? '1' : '0.6') + ';';

            if (hasAssignments) {
                unassignDiv.onmouseover = function () { unassignDiv.style.backgroundColor = '#fff5f5'; };
                unassignDiv.onmouseout = function () { unassignDiv.style.backgroundColor = '#fff'; };
                unassignDiv.addEventListener('click', function () { _confirmAndUnassign(); });
            }

            unassignDiv.innerHTML =
                '<div style="display:flex; align-items:center; gap:15px;">' +
                '<div style="width:6px; height:45px; background-color:' + (hasAssignments ? '#e74a3b' : '#ccc') + '; border-radius:3px;"></div>' +
                '<div>' +
                '<div style="font-weight:600; font-size:15px; color:' + (hasAssignments ? '#e74a3b' : '#888') + ';">Brak Automatyzacji (Odepnij)</div>' +
                '<div style="font-size:13px; color:#666; margin-top:2px;">' +
                (hasAssignments
                    ? 'Odepnij <strong>' + totalAssignedInSelection + '</strong> zaznaczonych produktów od ich obecnych reguł.'
                    : 'Żaden z zaznaczonych produktów nie jest przypisany do reguły.') +
                '</div></div></div>' +
                '<button class="Button-Page-Small-r" type="button" style="pointer-events:none;' + (!hasAssignments ? ' background-color:#ccc; border-color:#ccc;' : '') + '">Odepnij</button>';
            container.appendChild(unassignDiv);

            if (!rules || rules.length === 0) {
                var filterTypeParam = isAllegro ? '&filterType=1' : '';
                var emptyDiv = document.createElement('div');
                emptyDiv.innerHTML =
                    '<div class="alert alert-warning" style="text-align:center;">' +
                    'Brak zdefiniowanych reguł. <a href="/AutomationRules/Index?storeId=' + storeId + filterTypeParam + '" target="_blank">Utwórz nową</a>.' +
                    '</div>';
                container.appendChild(emptyDiv);
                return;
            }

            rules.sort(function (a, b) { return a.name.localeCompare(b.name, 'pl', { sensitivity: 'base' }); });

            rules.forEach(function (rule) {
                var statusColor = rule.isActive ? '#1cc88a' : '#e74a3b';
                var statusText = rule.isActive ? 'Aktywna' : 'Nieaktywna';
                var strategyIcon = rule.strategyMode === 0
                    ? '<i class="fa-solid fa-bolt" style="color:#888;"></i>'
                    : '<i class="fa-solid fa-dollar-sign" style="color:#888;"></i>';
                var strategyName = rule.strategyMode === 0 ? 'Lider Rynku' : 'Rentowność';

                var globalTotalInRule = rule.totalCount;
                var selectedAlreadyInRule = rule.matchingCount;
                var toBeAdded = totalSelected - selectedAlreadyInRule;

                var backgroundStyle = selectedAlreadyInRule > 0 ? '#fcfcfc' : '#fff';
                var borderStyle = '#e3e6f0';

                var div = document.createElement('div');
                div.className = 'automation-rule-item';
                div.style.cssText =
                    'border:1px solid ' + borderStyle + '; border-radius:8px; padding:12px 15px; ' +
                    'cursor:pointer; background:' + backgroundStyle + '; ' +
                    'display:flex; justify-content:space-between; align-items:center; ' +
                    'transition:background-color 0.2s, border-color 0.2s; margin-bottom:10px;';

                div.onmouseover = function () { div.style.backgroundColor = '#f8f9fc'; div.style.borderColor = '#b7b9cc'; };
                div.onmouseout = function () { div.style.background = backgroundStyle; div.style.borderColor = borderStyle; };

                div.innerHTML =
                    '<div style="display:flex; align-items:center; gap:15px; flex-grow:1;">' +
                    '<div style="width:6px; height:45px; background-color:' + rule.colorHex + '; border-radius:3px; flex-shrink:0;"></div>' +
                    '<div style="flex-grow:1;">' +

                    '<div style="display:flex; justify-content:space-between; align-items:center;">' +
                    '<div style="font-weight:600; font-size:16px; color:#333;">' + rule.name + '</div>' +
                    '<div style="font-size:12px; color:#888; display:flex; align-items:center; gap:8px;">' +
                    '<span style="display:flex; align-items:center; gap:4px;">' + strategyIcon + ' ' + strategyName + '</span>' +
                    '<span style="color:#e3e6f0;">|</span>' +
                    '<span style="color:' + statusColor + '; font-weight:500; display:flex; align-items:center; gap:4px;">' +
                    '<i class="fa-solid fa-circle" style="font-size:6px;"></i> ' + statusText +
                    '</span></div></div>' +

                    '<div style="display:flex; gap:15px; margin-top:6px; font-size:13px; color:#666; align-items:center; flex-wrap:wrap;">' +
                    '<span title="Całkowita liczba produktów w tej regule"><i class="fa-solid fa-database" style="color:#999; margin-right:4px;"></i> Razem: <strong>' + globalTotalInRule + '</strong></span>' +
                    '<span style="color:#e3e6f0;">|</span>' +
                    '<span title="Ile z zaznaczonych jest tutaj"><i class="fa-solid fa-check-double" style="color:#999; margin-right:4px;"></i> Wybranych: <strong>' + selectedAlreadyInRule + '</strong></span>' +
                    '<span style="color:#e3e6f0;">|</span>' +
                    '<span title="Ile zostanie dodanych">Zostanie dodanych: <strong style="color:#1cc88a;">+' + toBeAdded + '</strong></span>' +
                    '</div>' +

                    '</div></div>' +
                    '<div style="margin-left:20px;">' +
                    '<button class="Button-Page-Small-bl assign-rule-btn" type="button" style="pointer-events:none; white-space:nowrap; padding:5px 15px;">Wybierz</button>' +
                    '</div>';

                div.addEventListener('click', function () {
                    _confirmAndAssign(rule.id, rule.name);
                });

                container.appendChild(div);
            });
        }

        function _confirmAndAssign(ruleId, ruleName) {
            var label = isAllegro ? 'produktów Allegro' : 'produktów';
            if (!confirm('Czy na pewno chcesz przypisać ' + selectedProductIds.size + ' ' + label + ' do grupy "' + ruleName + '"?\n\nJeśli produkty były w innych grupach, zostaną przeniesione.')) {
                return;
            }
            _executeAutomationAction('/AutomationRules/AssignProducts', { RuleId: ruleId });
        }

        function _confirmAndUnassign() {
            var label = isAllegro ? 'produktów Allegro' : 'produktów';
            if (!confirm('Czy na pewno chcesz usunąć przypisanie do reguł automatyzacji dla ' + selectedProductIds.size + ' ' + label + '?')) {
                return;
            }
            _executeAutomationAction('/AutomationRules/UnassignProducts', {});
        }

        function _executeAutomationAction(url, extraData) {
            var productIdsArray = Array.from(selectedProductIds).map(function (id) { return parseInt(id); });

            $('#automationSelectionModal').modal('hide');
            showLoading();

            var payload = Object.assign({
                ProductIds: productIdsArray,
                IsAllegro: isAllegro
            }, extraData);

            fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            })
                .then(function (response) {
                    if (response.ok) return response.json();
                    return response.text().then(function (text) { throw new Error(text); });
                })
                .then(function (data) {
                    _askContinueOrFinish(onAutomationsUpdated);
                })
                .catch(function (error) {
                    console.error('Błąd:', error);
                    showGlobalNotification('<p style="font-weight:bold;">Błąd</p><p>' + error.message + '</p>');
                    setTimeout(function () { $('#automationSelectionModal').modal('show'); }, 500);
                })
                .finally(function () { hideLoading(); });
        }

     

        function _initExtraActions() {
            var btnContainer = document.getElementById('massActionExtraButtons');
            var modalContainer = document.getElementById('massActionExtraModals');
            if (!btnContainer || extraActions.length === 0) return;

            extraActions.forEach(function (action) {
                // ── Przycisk ──
                var btn = document.createElement('button');
                btn.id = 'massExtra_' + action.id;
                btn.className = 'mass-btn';
                btn.style.cssText = action.btnStyle || 'background-color:#858796; border:1px solid #858796; color:#fff;';

                var iconHtml = action.icon
                    ? '<i class="fas ' + action.icon + '"></i> '
                    : '';
                btn.innerHTML = iconHtml + action.label;

                if (action.btnHoverStyle) {
                    var origStyle = btn.style.cssText;
                    btn.addEventListener('mouseover', function () { btn.style.cssText = action.btnHoverStyle; });
                    btn.addEventListener('mouseout', function () { btn.style.cssText = origStyle; });
                }

                btn.addEventListener('click', function () {
                    if (selectedProductIds.size === 0) {
                        alert('Nie zaznaczono żadnych produktów.');
                        return;
                    }
                    if (typeof action.onClick === 'function') {
                        action.onClick({
                            selectedIds: Array.from(selectedProductIds).map(function (id) { return parseInt(id); }),
                            count: selectedProductIds.size,
                            closeMainModal: function () { $('#selectedProductsModal').modal('hide'); },
                            showLoading: showLoading,
                            hideLoading: hideLoading,
                            askContinueOrFinish: _askContinueOrFinish,
                            clearSelection: clearSelection
                        });
                    }
                });

                btnContainer.appendChild(btn);

                // ── Modal (opcjonalny) ──
                if (action.modalHtml && modalContainer) {
                    var wrapper = document.createElement('div');
                    wrapper.innerHTML = action.modalHtml;
                    while (wrapper.firstChild) {
                        modalContainer.appendChild(wrapper.firstChild);
                    }
                }
            });
        }

        function init() {

            var selectAllBtn = document.getElementById('selectAllVisibleBtn');
            var deselectAllBtn = document.getElementById('deselectAllVisibleBtn');
            if (selectAllBtn) selectAllBtn.addEventListener('click', selectAllVisible);
            if (deselectAllBtn) deselectAllBtn.addEventListener('click', deselectAllVisible);

            var showSelectedBtn = document.getElementById('showSelectedProductsBtn');
            if (showSelectedBtn) {
                showSelectedBtn.addEventListener('click', function () {
                    updateSelectionUI();
                    $('#selectedProductsModal').modal('show');
                });
            }

            var flagBtn = document.getElementById('openBulkFlagModalBtn');
            if (flagBtn) {
                flagBtn.addEventListener('click', function () { _openFlagModal(); });
            }

            var saveFlagsBtn = document.getElementById('saveFlagsButton');
            if (saveFlagsBtn) {
                saveFlagsBtn.addEventListener('click', function () { _saveFlags(); });
            }

            document.body.addEventListener('click', function (event) {
                var targetBtn = event.target.closest('#openBulkAutomationModalBtn');
                if (targetBtn) {
                    _openAutomationModal();
                }
            });

            var continueYesBtn = document.getElementById('continueActionsYesBtn');
            var continueNoBtn = document.getElementById('continueActionsNoBtn');
            if (continueYesBtn) continueYesBtn.addEventListener('click', function () { _handleContinueYes(); });
            if (continueNoBtn) continueNoBtn.addEventListener('click', function () { _handleContinueNo(); });

            var clearAllBtn = document.getElementById('clearAllSelectionBtn');
            if (clearAllBtn) {
                clearAllBtn.addEventListener('click', function () {
                    if (selectedProductIds.size === 0) return;
                    if (!confirm('Czy na pewno chcesz wyczyścić wszystkie zaznaczone produkty (' + selectedProductIds.size + ')?')) return;
                    clearSelection();
                    _flushPendingRefreshes();
                    $('#selectedProductsModal').modal('hide');
                });
            }

            $('#selectedProductsModal').on('hidden.bs.modal', function () {
                if (_pendingRefreshCallbacks.length > 0) {
                    _flushPendingRefreshes();
                }
            });

            // ═══ NOWE: Inicjalizacja dodatkowych akcji ═══
            _initExtraActions();

            updateSelectionUI();
            updateVisibleButtons();
        }

        return {
            init: init,

            isSelected: isSelected,
            getSelected: getSelected,
            getCount: getCount,
            addProduct: addProduct,
            removeProduct: removeProduct,
            toggleProduct: toggleProduct,
            selectAllVisible: selectAllVisible,
            deselectAllVisible: deselectAllVisible,
            clearSelection: clearSelection,

            updateCounters: updateCounters,
            updateSelectionUI: updateSelectionUI,
            updateVisibleButtons: updateVisibleButtons
        };
    }

    window.MassActions = MassActions;
})();