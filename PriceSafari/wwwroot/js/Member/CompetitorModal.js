//window.currentPreset = null;

//function showLoading() {
//    document.getElementById("loadingOverlay").style.display = "flex";
//}

//function hideLoading() {
//    document.getElementById("loadingOverlay").style.display = "none";
//}
//window.openCompetitorsModal = async function () {

//    window.presetHasChanged = false;

//    const competitorModal = document.getElementById("competitorModal");
//    if (!competitorModal) return;

//    competitorModal.style.display = 'block';

//    setTimeout(() => competitorModal.classList.add('show'), 10);

//    document.body.classList.add('modal-open');
//    if (!document.querySelector('.modal-backdrop')) {
//        const backdrop = document.createElement('div');
//        backdrop.className = 'modal-backdrop fade show';
//        document.body.appendChild(backdrop);
//    }

//    showLoading();

//    try {

//        await refreshPresetDropdown();

//        const presetSelect = document.getElementById("presetSelect");

//        let preselectedId = null;

//        if (window.presetContext === 'automationRule') {
//            const hiddenInput = document.getElementById('CompetitorPresetId');
//            if (hiddenInput && hiddenInput.value && hiddenInput.value !== "0") {
//                preselectedId = hiddenInput.value;
//            }
//        } else {
//            if (window.currentPreset && window.currentPreset.presetId) {
//                preselectedId = window.currentPreset.presetId;
//            }
//        }

//        const optionExists = preselectedId && Array.from(presetSelect.options).some(o => o.value == preselectedId);

//        if (optionExists) {

//            console.log("Ładowanie wybranego presetu:", preselectedId);
//            presetSelect.value = preselectedId;

//            await loadSelectedPreset(preselectedId);
//        } else {

//            const activeOption = Array.from(presetSelect.options).find(opt =>
//                opt.value !== "BASE" && opt.textContent.includes("[aktywny]")
//            );

//            if (activeOption && window.presetContext !== 'automationRule') {
//                console.log("Ładowanie aktywnego presetu:", activeOption.value);
//                presetSelect.value = activeOption.value;
//                await loadSelectedPreset(activeOption.value);
//            } else {
//                console.log("Ładowanie widoku BASE");
//                presetSelect.value = "BASE";
//                await loadBaseView();
//            }
//        }

//    } catch (error) {
//        console.error("Błąd w openCompetitorsModal:", error);
//        alert("Wystąpił błąd podczas otwierania okna.");
//    } finally {

//        hideLoading();
//    }
//};

//async function fetchPresets() {
//    const url = `/api/Presets/list/${storeId}?type=${presetTypeContext}`;
//    try {
//        const resp = await fetch(url);
//        return await resp.json();
//    } catch (err) {
//        console.error("fetchPresets error", err);
//        return [];
//    }
//}

//async function refreshPresetDropdown() {
//    const newPresets = await fetchPresets();
//    const presetSelect = document.getElementById("presetSelect");
//    if (!presetSelect) return;

//    presetSelect.innerHTML = "";

//    const anyActive = newPresets.some(p => p.nowInUse === true);

//    const baseOpt = document.createElement("option");
//    baseOpt.value = "BASE";
//    if (!anyActive) {
//        baseOpt.textContent = "Widok PriceSafari [aktywny]";
//    } else {
//        baseOpt.textContent = "Widok PriceSafari";
//    }
//    presetSelect.appendChild(baseOpt);

//    newPresets.forEach(p => {
//        const opt = document.createElement("option");
//        opt.value = p.presetId;

//        let suffix = "";
//        if (window.presetContext === 'automationRule') {

//        } else {

//            if (p.nowInUse) suffix = " [aktywny]";
//        }

//        opt.textContent = p.presetName + suffix;
//        presetSelect.appendChild(opt);
//    });

//    if (window.currentPreset && window.currentPreset.presetId) {
//        presetSelect.value = window.currentPreset.presetId;
//    } else {
//        presetSelect.value = "BASE";
//    }
//}

//document.addEventListener("change", async function (event) {

//    if (event.target && event.target.id === "presetSelect") {
//        const val = event.target.value;
//        if (val === "BASE") {
//            loadBaseView();
//        } else {
//            await loadSelectedPreset(val);
//        }
//    }

//    if (event.target && event.target.id === "useUnmarkedStoresCheckbox") {
//        const checkbox = event.target;
//        if (!window.currentPreset || window.currentPreset.presetId === null) {
//            if (window.currentPreset) {
//                window.currentPreset.useUnmarkedStores = checkbox.checked;
//                showLoading();
//                markPresetCompetitors();
//                hideLoading();
//            }
//            return;
//        }

//        window.currentPreset.useUnmarkedStores = checkbox.checked;
//        showLoading();
//        await saveOrUpdatePreset();
//        markPresetCompetitors();
//        hideLoading();
//    }

//    if (event.target && (event.target.id === "googleCheckbox" || event.target.id === "ceneoCheckbox")) {
//        const googleChk = document.getElementById("googleCheckbox");
//        const ceneoChk = document.getElementById("ceneoCheckbox");

//        if (googleChk && ceneoChk) {
//            if (!window.currentPreset || window.currentPreset.presetId === null) {
//                const val = determineSourceVal(googleChk.checked, ceneoChk.checked);
//                showLoading();
//                await loadCompetitors();
//                hideLoading();
//                return;
//            }

//            window.currentPreset.sourceGoogle = googleChk.checked;
//            window.currentPreset.sourceCeneo = ceneoChk.checked;
//            showLoading();
//            await saveOrUpdatePreset();
//            await loadCompetitors();
//            hideLoading();
//        }
//    }
//});

//document.addEventListener("input", function (event) {
//    if (event.target && event.target.id === "competitorSearchInput") {
//        filterCompetitors(event.target.value);
//    }
//});

//async function deactivateAllPresets() {
//    try {
//        const resp = await fetch(`/api/Presets/deactivate-all/${storeId}`, {
//            method: "POST",
//            headers: { 'Content-Type': 'application/json' }
//        });

//        if (!resp.ok) {

//            const errorText = await resp.text();
//            console.error("Błąd deaktywacji:", errorText);
//            alert("Błąd serwera podczas deaktywacji presetów.");
//            return;
//        }

//        const data = await resp.json();

//        if (!data.success) {
//            alert("Błąd deaktywacji presetów: " + (data.message || ""));
//        } else {

//            window.presetHasChanged = true;
//        }

//    } catch (err) {
//        console.error("deactivateAllPresets error", err);
//    }
//}

//async function isAnyCustomPresetActive() {
//    const presets = await fetchPresets();
//    return presets.some(p => p.nowInUse === true);
//}

//async function loadBaseView() {
//    showLoading();
//    try {
//        const customActive = await isAnyCustomPresetActive();

//        window.currentPreset = {
//            presetId: null,
//            storeId: storeId,
//            presetName: "Widok PriceSafari",
//            nowInUse: !customActive,
//            sourceGoogle: true,
//            sourceCeneo: true,
//            useUnmarkedStores: true,
//            competitors: []
//        };

//        const newPresetSection = document.getElementById("newPresetSection");
//        if (newPresetSection) newPresetSection.style.display = "block";

//        const presetNameInput = document.getElementById("presetNameInput");
//        if (presetNameInput) presetNameInput.style.display = "none";

//        const editBtn = document.getElementById("editPresetBtn");
//        if (editBtn) editBtn.style.display = "none";

//        const deleteBtn = document.getElementById("deletePresetBtn");
//        if (deleteBtn) deleteBtn.style.display = "none";

//        if (presetTypeContext === 0) {
//            const googleCheckbox = document.getElementById("googleCheckbox");
//            if (googleCheckbox) {
//                googleCheckbox.checked = window.currentPreset.sourceGoogle;
//                googleCheckbox.disabled = true;
//            }

//            const ceneoCheckbox = document.getElementById("ceneoCheckbox");
//            if (ceneoCheckbox) {
//                ceneoCheckbox.checked = window.currentPreset.sourceCeneo;
//                ceneoCheckbox.disabled = true;
//            }
//        }

//        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
//        if (useUnmarkedStoresCheckbox) {
//            useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
//            useUnmarkedStoresCheckbox.disabled = true;
//        }

//        let activateBtn = document.getElementById("activatePresetBtn");
//        if (!activateBtn) {
//            const container = document.getElementById("activateButtonContainer");
//            if (container) {
//                activateBtn = document.createElement("button");
//                activateBtn.id = "activatePresetBtn";
//                container.appendChild(activateBtn);
//            }
//        }

//        if (activateBtn) {
//            activateBtn.className = "Button-Page-Small";

//            if (window.presetContext === 'automationRule') {

//                const currentSelectedId = document.getElementById('CompetitorPresetId') ? document.getElementById('CompetitorPresetId').value : null;

//                const isSelected = (!currentSelectedId || currentSelectedId === "");

//                if (isSelected) {
//                    activateBtn.textContent = "Wybrany";
//                    activateBtn.disabled = true;
//                    activateBtn.classList.add("active-preset");
//                    activateBtn.onclick = null;
//                } else {
//                    activateBtn.textContent = "Wybierz ten widok";
//                    activateBtn.disabled = false;
//                    activateBtn.classList.remove("active-preset");

//                    activateBtn.onclick = function () {

//                        if (typeof window.selectPresetForAutomation === 'function') {

//                            window.selectPresetForAutomation('', 'Domyślny (Wszystkie sklepy)');
//                        }
//                    };
//                }

//            } else {

//                if (window.currentPreset.nowInUse) {
//                    activateBtn.textContent = "Aktywny";
//                    activateBtn.disabled = true;
//                    activateBtn.classList.add("active-preset");
//                } else {
//                    activateBtn.classList.remove("active-preset");
//                    activateBtn.textContent = "Ustaw jako aktywny";
//                    activateBtn.disabled = false;
//                    activateBtn.onclick = async function () {
//                        await deactivateAllPresets();
//                        window.presetHasChanged = true;
//                        await loadBaseView();
//                        await refreshPresetDropdown();
//                    };
//                }
//            }
//        }

//        await loadCompetitors();
//    } catch (err) {
//        console.error("loadBaseView error", err);
//    } finally {
//        hideLoading();
//    }
//}

//async function loadSelectedPreset(presetId) {
//    showLoading();
//    if (!presetId) {
//        const presetSelect = document.getElementById("presetSelect");
//        if (presetSelect) presetId = presetSelect.value;
//    }
//    if (!presetId || presetId === "BASE") {
//        await loadBaseView();
//        hideLoading();
//        return;
//    }
//    try {
//        const resp = await fetch(`/api/Presets/details/${presetId}`);
//        const preset = await resp.json();

//        window.currentPreset = {
//            presetId: preset.presetId,
//            storeId: storeId,
//            presetName: preset.presetName,
//            type: preset.type,
//            nowInUse: preset.nowInUse,
//            sourceGoogle: preset.sourceGoogle,
//            sourceCeneo: preset.sourceCeneo,
//            useUnmarkedStores: preset.useUnmarkedStores,
//            competitors: (preset.competitorItems || []).map(ci => ({
//                storeName: ci.storeName,
//                dataSource: ci.dataSource,
//                useCompetitor: ci.useCompetitor
//            }))
//        };

//        const newPresetSection = document.getElementById("newPresetSection");
//        if (newPresetSection) {
//            newPresetSection.style.display = "block";
//        }

//        const presetNameInput = document.getElementById("presetNameInput");
//        if (presetNameInput) {
//            presetNameInput.style.display = "block";
//            presetNameInput.value = preset.presetName || "";
//            presetNameInput.disabled = false;
//        }

//        let activateBtn = document.getElementById("activatePresetBtn");
//        if (!activateBtn) {
//            const container = document.getElementById("activateButtonContainer");
//            if (container) {
//                activateBtn = document.createElement("button");
//                activateBtn.id = "activatePresetBtn";
//                container.appendChild(activateBtn);
//            }
//        }

//        if (activateBtn) {
//            activateBtn.className = "Button-Page-Small";

//            if (window.presetContext === 'automationRule') {

//                const inputEl = document.getElementById('CompetitorPresetId');
//                const currentSelectedId = inputEl ? inputEl.value : "";

//                const isSelected = (currentSelectedId !== "" && currentSelectedId == window.currentPreset.presetId);

//                if (isSelected) {
//                    activateBtn.textContent = "Wybrany";
//                    activateBtn.disabled = true;
//                    activateBtn.classList.add("active-preset");
//                    activateBtn.onclick = null;
//                } else {
//                    activateBtn.textContent = "Wybierz ten preset";
//                    activateBtn.disabled = false;
//                    activateBtn.classList.remove("active-preset");

//                    activateBtn.onclick = function () {
//                        if (typeof window.selectPresetForAutomation === 'function') {
//                            window.selectPresetForAutomation(window.currentPreset.presetId, window.currentPreset.presetName);
//                        }
//                    };
//                }

//            } else {

//                if (window.currentPreset.nowInUse) {
//                    activateBtn.textContent = "Aktywny";
//                    activateBtn.disabled = true;
//                    activateBtn.classList.add("active-preset");
//                } else {
//                    activateBtn.textContent = "Ustaw jako aktywny";
//                    activateBtn.disabled = false;
//                    activateBtn.classList.remove("active-preset");
//                    activateBtn.onclick = function () {
//                        window.currentPreset.nowInUse = true;
//                        saveOrUpdatePreset().then(() => {
//                            refreshPresetDropdown();
//                        });
//                        activateBtn.textContent = "Aktywny";
//                        activateBtn.disabled = true;
//                        activateBtn.classList.add("active-preset");
//                    };
//                }
//            }
//        }

//        let editBtn = document.getElementById("editPresetBtn");
//        const editContainer = document.getElementById("editButtonContainer");
//        if (editContainer && !editBtn) {
//            editBtn = document.createElement("button");
//            editBtn.id = "editPresetBtn";
//            editContainer.appendChild(editBtn);
//        }
//        if (editBtn) {
//            editBtn.innerHTML = '<i class="fas fa-pen" style="color:#4e4e4e; font-size:16px;"></i>';
//            editBtn.title = "Zmień nazwę presetu";
//            editBtn.style.border = "none";
//            editBtn.style.borderRadius = "4px";
//            editBtn.style.width = "33px";
//            editBtn.style.height = "33px";
//            editBtn.style.background = "#e3e3e3";
//            editBtn.style.cursor = "pointer";
//            editBtn.style.display = "inline-block";
//            editBtn.onclick = async function () {
//                if (!window.currentPreset || !window.currentPreset.presetId) {
//                    alert("Nie można zmienić nazwy presetu.");
//                    return;
//                }
//                let newName = prompt("Podaj nową nazwę presetu (max 50 znaków):", window.currentPreset.presetName);
//                if (newName && newName.trim() !== "") {
//                    newName = newName.trim();
//                    if (newName.length > 50) {
//                        newName = newName.substring(0, 50);
//                        alert("Nazwa została przycięta do 50 znaków.");
//                    }
//                    window.currentPreset.presetName = newName;
//                    await saveOrUpdatePreset();
//                    await refreshPresetDropdown();
//                    alert("Zmieniono nazwę presetu.");
//                }
//            };
//        }

//        let deleteBtn = document.getElementById("deletePresetBtn");
//        const deleteContainer = document.getElementById("deleteButtonContainer");
//        if (deleteContainer && !deleteBtn) {
//            deleteBtn = document.createElement("button");
//            deleteBtn.id = "deletePresetBtn";
//            deleteContainer.appendChild(deleteBtn);
//        }
//        if (deleteBtn) {
//            deleteBtn.innerHTML = '<i class="fa fa-trash" style="color:red; font-size:20px;"></i>';
//            deleteBtn.title = "Usuń preset";
//            deleteBtn.style.border = "none";
//            deleteBtn.style.borderRadius = "4px";
//            deleteBtn.style.width = "33px";
//            deleteBtn.style.height = "33px";
//            deleteBtn.style.background = "#e3e3e3";
//            deleteBtn.style.cursor = "pointer";
//            deleteBtn.style.display = "inline-block";

//            deleteBtn.onclick = async function () {
//                if (confirm("Czy na pewno chcesz usunąć ten preset?")) {
//                    try {
//                        const deletedPresetId = window.currentPreset.presetId;
//                        const resp = await fetch(`/api/Presets/delete/${deletedPresetId}`, {
//                            method: "POST",
//                            headers: { 'Content-Type': 'application/json' }
//                        });

//                        if (!resp.ok) {
//                            alert("Błąd serwera podczas usuwania presetu.");
//                            return;
//                        }

//                        const data = await resp.json();
//                        if (data.success) {
//                            alert("Preset został usunięty.");
//                            window.presetHasChanged = true;

//                            if (window.presetContext === 'automationRule') {
//                                const hiddenInput = document.getElementById('CompetitorPresetId');
//                                if (hiddenInput && hiddenInput.value == deletedPresetId) {
//                                    hiddenInput.value = '';
//                                    const labelName = document.getElementById('selectedPresetName');
//                                    if (labelName) labelName.innerText = "Domyślny (Wszystkie sklepy)";
//                                }
//                            }

//                            await refreshPresetDropdown();
//                            document.getElementById("presetSelect").value = "BASE";
//                            await loadBaseView();
//                        } else {
//                            alert("Błąd: " + data.message);
//                        }
//                    } catch (err) {
//                        console.error("deletePreset error", err);
//                    }
//                }
//            };
//        }

//        if (presetTypeContext === 0) {
//            const googleChk = document.getElementById("googleCheckbox");
//            if (googleChk) {
//                googleChk.checked = !!preset.sourceGoogle;
//                googleChk.disabled = false;
//            }
//            const ceneoChk = document.getElementById("ceneoCheckbox");
//            if (ceneoChk) {
//                ceneoChk.checked = !!preset.sourceCeneo;
//                ceneoChk.disabled = false;
//            }
//        }

//        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
//        if (useUnmarkedStoresCheckbox) {
//            useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
//            useUnmarkedStoresCheckbox.disabled = false;
//        }

//        await loadCompetitors();

//    } catch (err) {
//        console.error("loadSelectedPreset error", err);
//    } finally {
//        hideLoading();
//    }
//}

//function determineSourceVal(isGoogle, isCeneo) {
//    if (isGoogle && isCeneo) return "All";
//    if (isGoogle) return "Google";
//    if (isCeneo) return "Ceneo";
//    return "All";
//}

//async function loadCompetitors() {
//    try {
//        let url = '';
//        if (presetTypeContext === 0) {
//            const sourceVal = determineSourceVal(
//                document.getElementById("googleCheckbox").checked,
//                document.getElementById("ceneoCheckbox").checked
//            );
//            url = `/api/Presets/competitor-data/${storeId}?ourSource=${sourceVal}`;
//        } else if (presetTypeContext === 1) {
//            url = `/api/Presets/allegro-competitors/${storeId}`;
//        } else {
//            return;
//        }

//        const response = await fetch(url);
//        const data = await response.json();

//        if (!data.data) {
//            console.warn("Brak danych konkurentów");
//            return;
//        }

//        if (presetTypeContext === 0) {
//            const googleData = data.data.filter(x => x.dataSource === "Google");
//            const ceneoData = data.data.filter(x => x.dataSource === "Ceneo");

//            document.getElementById("googleCompetitorsTableBody").innerHTML = "";
//            document.getElementById("ceneoCompetitorsTableBody").innerHTML = "";
//            googleData.forEach(i => document.getElementById("googleCompetitorsTableBody").appendChild(createRow(i)));
//            ceneoData.forEach(i => document.getElementById("ceneoCompetitorsTableBody").appendChild(createRow(i)));

//        } else if (presetTypeContext === 1) {

//            const allegroBody = document.getElementById("allegroCompetitorsTableBody");
//            if (allegroBody) {
//                allegroBody.innerHTML = "";
//                data.data.forEach(i => allegroBody.appendChild(createRow(i)));
//            }
//        }

//        markPresetCompetitors();

//    } catch (err) {
//        console.error("Błąd w loadCompetitors:", err);
//    }
//}

//function createRow(item) {
//    const tr = document.createElement("tr");
//    tr.dataset.originalStoreName = item.storeName;
//    tr.dataset.storeName = item.storeName;
//    tr.dataset.dataSource = item.dataSource;

//    const tdStore = document.createElement("td");
//    tdStore.textContent = item.storeName;
//    tr.appendChild(tdStore);

//    const tdCommon = document.createElement("td");
//    tdCommon.textContent = item.commonProductsCount;
//    tr.appendChild(tdCommon);

//    let sourceActive = true;
//    if (presetTypeContext === 0) {
//        if (item.dataSource === "Google" && !window.currentPreset.sourceGoogle) {
//            sourceActive = false;
//        }
//        if (item.dataSource === "Ceneo" && !window.currentPreset.sourceCeneo) {
//            sourceActive = false;
//        }
//    }

//    const tdAction = document.createElement("td");
//    if (!sourceActive) {
//        tr.style.backgroundColor = "#cccccc";
//        tdAction.textContent = "Niedostępne";
//    } else {

//        const isGoogleForButtons = item.dataSource === 'Google';

//        const addBtn = document.createElement("button");
//        addBtn.className = "filterCompetitors-true";
//        addBtn.style.marginRight = "5px";
//        addBtn.addEventListener("click", () => {
//            toggleCompetitorUsage(item.storeName, isGoogleForButtons, true);
//        });
//        addBtn.innerHTML = '<i class="fas fa-check"></i>';
//        tdAction.appendChild(addBtn);

//        const remBtn = document.createElement("button");
//        remBtn.className = "filterCompetitors-false";
//        remBtn.style.marginRight = "5px";
//        remBtn.addEventListener("click", () => {
//            toggleCompetitorUsage(item.storeName, isGoogleForButtons, false);
//        });
//        remBtn.innerHTML = '<i class="fas fa-times"></i>';
//        tdAction.appendChild(remBtn);

//        const clearBtn = document.createElement("button");
//        clearBtn.className = "filterCompetitors-back";
//        clearBtn.addEventListener("click", () => {
//            clearCompetitorUsage(item.storeName, isGoogleForButtons);
//        });
//        clearBtn.innerHTML = '<i class="fas fa-undo"></i>';
//        tdAction.appendChild(clearBtn);
//    }
//    tr.appendChild(tdAction);
//    return tr;
//}

//function toggleCompetitorUsage(storeName, isGoogle, useCompetitor) {
//    if (!window.currentPreset || !window.currentPreset.presetId) {
//        alert("Stwórz własny preset, aby wprowadzać zmiany.");
//        return;
//    }

//    let dataSourceValue;
//    if (presetTypeContext === 0) {
//        dataSourceValue = isGoogle ? 0 : 1;
//    } else {
//        dataSourceValue = 2;
//    }

//    let comp = window.currentPreset.competitors.find(c =>
//        c.storeName.toLowerCase() === storeName.toLowerCase() && c.dataSource === dataSourceValue
//    );

//    if (!comp) {
//        comp = { storeName, dataSource: dataSourceValue, useCompetitor };
//        window.currentPreset.competitors.push(comp);
//    } else {
//        comp.useCompetitor = useCompetitor;
//    }

//    saveOrUpdatePreset();
//    refreshRowColor(storeName, isGoogle);
//}

//function clearCompetitorUsage(storeName, isGoogle) {
//    if (!window.currentPreset || !window.currentPreset.presetId) {
//        alert("Stwórz własny preset, aby wprowadzać zmiany.");
//        return;
//    }

//    let dataSourceValue;
//    if (presetTypeContext === 0) {
//        dataSourceValue = isGoogle ? 0 : 1;
//    } else {
//        dataSourceValue = 2;
//    }

//    const idx = window.currentPreset.competitors.findIndex(c =>
//        c.storeName.toLowerCase() === storeName.toLowerCase() && c.dataSource === dataSourceValue
//    );

//    if (idx !== -1) {
//        window.currentPreset.competitors.splice(idx, 1);
//    }

//    saveOrUpdatePreset();
//    refreshRowColor(storeName, isGoogle);
//}

//function markPresetCompetitors() {
//    if (presetTypeContext === 0) {
//        document.querySelectorAll("#googleCompetitorsTableBody tr").forEach(tr => {
//            refreshRowColor(tr.dataset.storeName, true);
//        });
//        document.querySelectorAll("#ceneoCompetitorsTableBody tr").forEach(tr => {
//            refreshRowColor(tr.dataset.storeName, false);
//        });
//    } else if (presetTypeContext === 1) {
//        document.querySelectorAll("#allegroCompetitorsTableBody tr").forEach(tr => {
//            refreshRowColor(tr.dataset.storeName, null);
//        });
//    }
//}

//function refreshRowColor(storeName, isGoogle) {
//    let row;
//    let dataSourceValue;

//    if (presetTypeContext === 0) {
//        const ds = isGoogle ? "Google" : "Ceneo";
//        row = document.querySelector(`tr[data-store-name="${storeName}"][data-data-source="${ds}"]`);
//        dataSourceValue = isGoogle ? 0 : 1;
//    } else if (presetTypeContext === 1) {
//        row = document.querySelector(`tr[data-store-name="${storeName}"][data-data-source="Allegro"]`);
//        dataSourceValue = 2;
//    }

//    if (!row || !window.currentPreset) return;

//    if (presetTypeContext === 0) {
//        let sourceActive = true;
//        if (isGoogle && !window.currentPreset.sourceGoogle) sourceActive = false;
//        if (!isGoogle && !window.currentPreset.sourceCeneo) sourceActive = false;
//        if (!sourceActive) {

//            row.style.backgroundColor = "#cccccc";
//            return;
//        }
//    }

//    const item = window.currentPreset.competitors.find(ci =>
//        ci.storeName.toLowerCase() === storeName.toLowerCase() && ci.dataSource === dataSourceValue
//    );

//    const useUnmarked = window.currentPreset.useUnmarkedStores;
//    if (item) {
//        row.style.backgroundColor = item.useCompetitor ? "#7AD37A" : "#FC8686";
//    } else {
//        row.style.backgroundColor = useUnmarked ? "#D8FED8" : "#FFDDDD";
//    }
//}

//document.addEventListener("click", async function (event) {

//    if (event.target && event.target.id === "addNewPresetBtn") {
//        let presetName = prompt("Podaj nazwę nowego presetu (max 50 znaków):", "");
//        if (!presetName) return;

//        presetName = presetName.trim();
//        if (presetName.length > 50) {
//            presetName = presetName.substring(0, 50);
//            alert("Nazwa została przycięta do 50 znaków.");
//        }

//        const newPreset = {
//            presetId: 0,
//            storeId: storeId,
//            presetName,
//            type: presetTypeContext,
//            nowInUse: false,
//            sourceGoogle: true,
//            sourceCeneo: true,
//            useUnmarkedStores: true,
//            competitors: []
//        };

//        try {
//            const resp = await fetch("/api/Presets/save", {
//                method: "POST",
//                headers: { "Content-Type": "application/json" },
//                body: JSON.stringify(newPreset)
//            });
//            const data = await resp.json();
//            if (data.success) {
//                alert(`Utworzono nowy preset`);
//                await refreshPresetDropdown();

//                const presetSelect = document.getElementById("presetSelect");
//                if (presetSelect) presetSelect.value = data.presetId;

//                await loadSelectedPreset(data.presetId);
//            } else {
//                alert("Błąd tworzenia nowego presetu");
//            }
//        } catch (err) {
//            console.error("addNewPreset error:", err);
//        }
//    }
//});

//document.addEventListener("DOMContentLoaded", () => {
//    const editBtn = document.getElementById("editPresetBtn");
//    if (editBtn) {
//        editBtn.addEventListener("click", async function () {
//            if (!window.currentPreset || !window.currentPreset.presetId) {
//                alert("Nie można zmienić nazwy presetu.");
//                return;
//            }
//            let presetName = prompt("Podaj nazwę nowego presetu (max 50 znaków):", window.currentPreset.presetName);
//            if (!presetName) return;
//            presetName = presetName.trim();
//            if (presetName.length > 50) {
//                presetName = presetName.substring(0, 50);
//                alert("Nazwa została przycięta do 50 znaków.");
//            }
//            window.currentPreset.presetName = presetName;
//            await saveOrUpdatePreset();
//            await refreshPresetDropdown();
//            alert("Zmieniono nazwę presetu.");
//        });
//    }
//});

//document.addEventListener("DOMContentLoaded", () => {
//    const googleChk = document.getElementById("googleCheckbox");
//    const ceneoChk = document.getElementById("ceneoCheckbox");
//    if (googleChk && ceneoChk) {
//        async function onSourceChange() {

//            if (!window.currentPreset || window.currentPreset.presetId === null) {
//                const val = determineSourceVal(googleChk.checked, ceneoChk.checked);
//                showLoading();
//                await loadCompetitors(val);
//                hideLoading();
//                return;
//            }

//            window.currentPreset.sourceGoogle = googleChk.checked;
//            window.currentPreset.sourceCeneo = ceneoChk.checked;
//            showLoading();
//            await saveOrUpdatePreset();
//            const sourceVal = determineSourceVal(googleChk.checked, ceneoChk.checked);
//            await loadCompetitors(sourceVal);
//            hideLoading();
//        }
//        googleChk.addEventListener("change", onSourceChange);
//        ceneoChk.addEventListener("change", onSourceChange);
//    }
//});

//async function saveOrUpdatePreset() {
//    if (!window.currentPreset) return;
//    if (!window.currentPreset.presetId) return;
//    try {
//        const resp = await fetch("/api/Presets/save", {
//            method: "POST",
//            headers: { "Content-Type": "application/json" },
//            body: JSON.stringify(window.currentPreset)
//        });
//        const data = await resp.json();
//        if (!data.success) {
//            alert("Błąd zapisu presetów: " + (data.message || ""));
//            return;
//        }
//        window.currentPreset.presetId = data.presetId;
//        window.presetHasChanged = true;
//    } catch (err) {
//        console.error("saveOrUpdatePreset error", err);
//        alert("Błąd zapisu (SaveOrUpdatePreset). Sprawdź konsolę.");
//    }
//}

//function filterCompetitors(searchTerm) {
//    let selector;

//    if (presetTypeContext === 0) {
//        selector = "#googleCompetitorsTableBody tr, #ceneoCompetitorsTableBody tr";
//    } else if (presetTypeContext === 1) {
//        selector = "#allegroCompetitorsTableBody tr";
//    } else {

//        return;
//    }

//    const allRows = document.querySelectorAll(selector);

//    allRows.forEach(tr => {
//        const originalStoreName = tr.dataset.originalStoreName || "";
//        const storeNameLower = originalStoreName.toLowerCase();
//        const searchLower = searchTerm.toLowerCase();

//        const storeTd = tr.querySelector("td:nth-child(1)");
//        if (!storeTd) return;

//        if (!searchTerm || storeNameLower.includes(searchLower)) {
//            tr.style.display = "";
//            const highlighted = highlightTextCaseInsensitive(originalStoreName, searchTerm);
//            storeTd.innerHTML = highlighted;
//        } else {
//            tr.style.display = "none";
//            storeTd.textContent = originalStoreName;
//        }
//    });
//}

//function highlightTextCaseInsensitive(fullText, searchTerm) {
//    if (!searchTerm) return fullText;
//    const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
//    const regex = new RegExp(`(${escapedTerm})`, 'gi');
//    return fullText.replace(regex, '<span style="font-weight:bold; color:purple;">$1</span>');
//}

//document.addEventListener('keydown', function (event) {
//    if (event.key === "Escape") {
//        const modal = document.getElementById('competitorModal');

//        if (modal && modal.classList.contains('show')) {
//            modal.style.display = 'none';
//            modal.classList.remove('show');

//            if (typeof priceHistoryPageContext !== 'undefined') {
//                if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {

//                    loadPrices();
//                } else if (priceHistoryPageContext === 'details') {

//                    window.location.reload();
//                }
//            } else {

//            }

//        }
//    }
//});

//document.addEventListener("click", function (event) {

//    const closeBtn = event.target.closest("[data-dismiss='modal']");

//    if (closeBtn) {

//        const modal = closeBtn.closest(".modal-sim") || document.getElementById("competitorModal");

//        if (modal) {

//            modal.style.display = "none";
//            modal.classList.remove("show");

//            const backdrops = document.getElementsByClassName('modal-backdrop');
//            while (backdrops.length > 0) {
//                backdrops[0].parentNode.removeChild(backdrops[0]);
//            }
//            document.body.classList.remove('modal-open');

//            hideLoading();

//            if (modal.id === "competitorModal") {
//                if (window.presetHasChanged === true) {
//                    console.log("Zmiany w presetach wykryte - odświeżam widok.");

//                    if (typeof priceHistoryPageContext !== 'undefined') {
//                        if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
//                            loadPrices();

//                        } else if (priceHistoryPageContext === 'details') {
//                            window.location.reload();

//                        }
//                    }
//                } else {
//                    console.log("Brak zmian w presetach - nie odświeżam.");
//                }
//            }

//            else {
//                if (typeof priceHistoryPageContext !== 'undefined') {
//                    if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
//                        loadPrices();
//                    } else if (priceHistoryPageContext === 'details') {
//                        window.location.reload();
//                    }
//                }
//            }
//        }
//    }
//});

//document.addEventListener('keydown', function (event) {
//    if (event.key === "Escape") {
//        const modal = document.querySelector('.modal-sim.show') || document.getElementById('competitorModal');
//        if (modal && (modal.style.display === 'block' || modal.classList.contains('show'))) {
//            modal.style.display = 'none';
//            modal.classList.remove('show');
//            document.body.classList.remove('modal-open');
//        }
//    }
//});





















window.currentPreset = null;

function showLoading() {
    document.getElementById("loadingOverlay").style.display = "flex";
}

function hideLoading() {
    document.getElementById("loadingOverlay").style.display = "none";
}

// --- FUNKCJE SUWAKA (DODANE) ---
function updateDualSlider() {
    // Pobieramy elementy lokalnie, aby uniknąć problemów z ReferenceError
    const sliderMin = document.getElementById("deliverySliderMin");
    const sliderMax = document.getElementById("deliverySliderMax");
    const sliderRangeFill = document.getElementById("sliderRangeFill");
    const valMinDisplay = document.getElementById("rangeMinVal");
    const valMaxDisplay = document.getElementById("rangeMaxVal");

    if (!sliderMin || !sliderMax) return;

    let slide1 = parseInt(sliderMin.value);
    let slide2 = parseInt(sliderMax.value);

    // Zabezpieczenie przed przekroczeniem
    if (slide1 > slide2) {
        let tmp = slide1;
        sliderMin.value = slide2;
        sliderMax.value = tmp;
        slide1 = slide2;
        slide2 = tmp;
    }

    // Aktualizacja tekstów
    if (valMinDisplay) valMinDisplay.textContent = slide1 === 0 ? "Natychmiast" : slide1;
    if (valMaxDisplay) valMaxDisplay.textContent = slide2 >= 31 ? "31+" : slide2;

    // Aktualizacja paska koloru (fill)
    const percent1 = (slide1 / sliderMin.max) * 100;
    const percent2 = (slide2 / sliderMax.max) * 100;

    if (sliderRangeFill) {
        sliderRangeFill.style.left = percent1 + "%";
        sliderRangeFill.style.width = (percent2 - percent1) + "%";

        // Logika wizualna disabled
        if (sliderMin.disabled) {
            sliderRangeFill.style.backgroundColor = "#ccc";
        } else {
            sliderRangeFill.style.backgroundColor = "#5a5c69";
        }
    }

    // Aktualizacja obiektu currentPreset (jeśli istnieje)
    if (window.currentPreset) {
        window.currentPreset.minDeliveryDays = slide1;
        window.currentPreset.maxDeliveryDays = slide2;
    }
}
// ---------------------------------

window.openCompetitorsModal = async function () {
    window.presetHasChanged = false;

    const competitorModal = document.getElementById("competitorModal");
    if (!competitorModal) return;

    competitorModal.style.display = 'block';
    setTimeout(() => competitorModal.classList.add('show'), 10);

    document.body.classList.add('modal-open');
    if (!document.querySelector('.modal-backdrop')) {
        const backdrop = document.createElement('div');
        backdrop.className = 'modal-backdrop fade show';
        document.body.appendChild(backdrop);
    }

    showLoading();

    try {
        await refreshPresetDropdown();

        const presetSelect = document.getElementById("presetSelect");
        let preselectedId = null;

        // Priorytet 1: Automation Rule (jeśli w tym kontekście)
        if (window.presetContext === 'automationRule') {
            const hiddenInput = document.getElementById('CompetitorPresetId');
            if (hiddenInput && hiddenInput.value && hiddenInput.value !== "0") {
                preselectedId = hiddenInput.value;
            }
        }
        // Priorytet 2: Ostatnio używany w sesji JS
        else {
            if (window.currentPreset && window.currentPreset.presetId) {
                preselectedId = window.currentPreset.presetId;
            }
        }

        const optionExists = preselectedId && Array.from(presetSelect.options).some(o => o.value == preselectedId);

        if (optionExists) {
            console.log("Ładowanie wybranego presetu:", preselectedId);
            presetSelect.value = preselectedId;
            await loadSelectedPreset(preselectedId);
        } else {
            // Szukamy aktywnego (oznaczonego [aktywny] w tekście lub flagą w bazie)
            // Uwaga: refreshPresetDropdown ustawia tekst "[aktywny]", więc szukamy po tekście lub pobieramy dane z API
            // Najbezpieczniej: Sprawdźmy, czy jest opcja inna niż BASE z "[aktywny]"

            const activeOption = Array.from(presetSelect.options).find(opt =>
                opt.value !== "BASE" && opt.textContent.includes("[aktywny]")
            );

            if (activeOption && window.presetContext !== 'automationRule') {
                console.log("Ładowanie aktywnego presetu:", activeOption.value);
                presetSelect.value = activeOption.value;
                await loadSelectedPreset(activeOption.value);
            } else {
                console.log("Ładowanie widoku BASE");
                presetSelect.value = "BASE";
                await loadBaseView();
            }
        }

    } catch (error) {
        console.error("Błąd w openCompetitorsModal:", error);
        alert("Wystąpił błąd podczas otwierania okna.");
    } finally {
        hideLoading();
    }
};

async function fetchPresets() {
    const url = `/api/Presets/list/${storeId}?type=${presetTypeContext}`;
    try {
        const resp = await fetch(url);
        return await resp.json();
    } catch (err) {
        console.error("fetchPresets error", err);
        return [];
    }
}

async function refreshPresetDropdown() {
    const newPresets = await fetchPresets();
    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;

    presetSelect.innerHTML = "";

    const anyActive = newPresets.some(p => p.nowInUse === true);

    const baseOpt = document.createElement("option");
    baseOpt.value = "BASE";
    if (!anyActive) {
        baseOpt.textContent = "Widok PriceSafari [aktywny]";
    } else {
        baseOpt.textContent = "Widok PriceSafari";
    }
    presetSelect.appendChild(baseOpt);

    newPresets.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p.presetId;

        let suffix = "";
        if (window.presetContext !== 'automationRule') {
            if (p.nowInUse) suffix = " [aktywny]";
        }

        opt.textContent = p.presetName + suffix;
        presetSelect.appendChild(opt);
    });

    // Przywracamy zaznaczenie, jeśli mamy stan w JS
    if (window.currentPreset && window.currentPreset.presetId) {
        presetSelect.value = window.currentPreset.presetId;
    } else {
        // Jeśli nie mamy stanu, nie wymuszamy BASE tutaj, 
        // bo openCompetitorsModal zdecyduje co załadować.
    }
}

document.addEventListener("change", async function (event) {
    if (event.target && event.target.id === "presetSelect") {
        const val = event.target.value;
        if (val === "BASE") {
            loadBaseView();
        } else {
            await loadSelectedPreset(val);
        }
    }

    if (event.target && event.target.id === "useUnmarkedStoresCheckbox") {
        const checkbox = event.target;
        if (!window.currentPreset || window.currentPreset.presetId === null) {
            // Edycja BASE (tylko w pamięci JS dla widoku)
            if (window.currentPreset) {
                window.currentPreset.useUnmarkedStores = checkbox.checked;
                showLoading();
                markPresetCompetitors();
                hideLoading();
            }
            return;
        }

        window.currentPreset.useUnmarkedStores = checkbox.checked;
        showLoading();
        await saveOrUpdatePreset();
        markPresetCompetitors();
        hideLoading();
    }

    if (event.target && (event.target.id === "googleCheckbox" || event.target.id === "ceneoCheckbox")) {
        const googleChk = document.getElementById("googleCheckbox");
        const ceneoChk = document.getElementById("ceneoCheckbox");

        if (googleChk && ceneoChk) {
            if (!window.currentPreset || window.currentPreset.presetId === null) {
                const val = determineSourceVal(googleChk.checked, ceneoChk.checked);
                showLoading();
                await loadCompetitors();
                hideLoading();
                return;
            }

            window.currentPreset.sourceGoogle = googleChk.checked;
            window.currentPreset.sourceCeneo = ceneoChk.checked;
            showLoading();
            await saveOrUpdatePreset();
            await loadCompetitors();
            hideLoading();
        }
    }
});

document.addEventListener("input", function (event) {
    if (event.target && event.target.id === "competitorSearchInput") {
        filterCompetitors(event.target.value);
    }
});

async function deactivateAllPresets() {
    try {
        const resp = await fetch(`/api/Presets/deactivate-all/${storeId}`, {
            method: "POST",
            headers: { 'Content-Type': 'application/json' }
        });

        if (!resp.ok) {
            const errorText = await resp.text();
            console.error("Błąd deaktywacji:", errorText);
            alert("Błąd serwera podczas deaktywacji presetów.");
            return;
        }

        const data = await resp.json();
        if (!data.success) {
            alert("Błąd deaktywacji presetów: " + (data.message || ""));
        } else {
            window.presetHasChanged = true;
        }
    } catch (err) {
        console.error("deactivateAllPresets error", err);
    }
}

async function isAnyCustomPresetActive() {
    const presets = await fetchPresets();
    return presets.some(p => p.nowInUse === true);
}

async function loadBaseView() {
    showLoading();
    try {
        const customActive = await isAnyCustomPresetActive();

        window.currentPreset = {
            presetId: null,
            storeId: storeId,
            presetName: "Widok PriceSafari",
            nowInUse: !customActive,
            sourceGoogle: true,
            sourceCeneo: true,
            useUnmarkedStores: true,
            minDeliveryDays: 0,
            maxDeliveryDays: 31,
            competitors: []
        };

        const newPresetSection = document.getElementById("newPresetSection");
        if (newPresetSection) newPresetSection.style.display = "block";

        const presetNameInput = document.getElementById("presetNameInput");
        if (presetNameInput) presetNameInput.style.display = "none";

        const editBtn = document.getElementById("editPresetBtn");
        if (editBtn) editBtn.style.display = "none";

        const deleteBtn = document.getElementById("deletePresetBtn");
        if (deleteBtn) deleteBtn.style.display = "none";

        if (presetTypeContext === 0) {
            const googleCheckbox = document.getElementById("googleCheckbox");
            if (googleCheckbox) {
                googleCheckbox.checked = window.currentPreset.sourceGoogle;
                googleCheckbox.disabled = true;
            }
            const ceneoCheckbox = document.getElementById("ceneoCheckbox");
            if (ceneoCheckbox) {
                ceneoCheckbox.checked = window.currentPreset.sourceCeneo;
                ceneoCheckbox.disabled = true;
            }
        }

        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        if (useUnmarkedStoresCheckbox) {
            useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
            useUnmarkedStoresCheckbox.disabled = true;
        }

        // --- OBSŁUGA SUWAKA ---
        const deliverySection = document.getElementById("deliveryFilterSection");
        const sliderMin = document.getElementById("deliverySliderMin");
        const sliderMax = document.getElementById("deliverySliderMax");

        if (deliverySection) {
            if (presetTypeContext === 1) { // 1 = Marketplace (Allegro)
                deliverySection.style.display = "flex";
                if (sliderMin && sliderMax) {
                    sliderMin.value = 0;
                    sliderMax.value = 31;

                    // BLOKADA W WIDOKU BASE
                    sliderMin.disabled = true;
                    sliderMax.disabled = true;
                    deliverySection.classList.add("disabled-section");

                    updateDualSlider();
                }
            } else {
                deliverySection.style.display = "none";
            }
        }
        // ----------------------

        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            const container = document.getElementById("activateButtonContainer");
            if (container) {
                activateBtn = document.createElement("button");
                activateBtn.id = "activatePresetBtn";
                container.appendChild(activateBtn);
            }
        }

        if (activateBtn) {
            activateBtn.className = "Button-Page-Small";
            if (window.presetContext === 'automationRule') {
                const currentSelectedId = document.getElementById('CompetitorPresetId') ? document.getElementById('CompetitorPresetId').value : null;
                const isSelected = (!currentSelectedId || currentSelectedId === "");

                if (isSelected) {
                    activateBtn.textContent = "Wybrany";
                    activateBtn.disabled = true;
                    activateBtn.classList.add("active-preset");
                    activateBtn.onclick = null;
                } else {
                    activateBtn.textContent = "Wybierz ten widok";
                    activateBtn.disabled = false;
                    activateBtn.classList.remove("active-preset");
                    activateBtn.onclick = function () {
                        if (typeof window.selectPresetForAutomation === 'function') {
                            window.selectPresetForAutomation('', 'Domyślny (Wszystkie sklepy)');
                        }
                    };
                }
            } else {
                if (window.currentPreset.nowInUse) {
                    activateBtn.textContent = "Aktywny";
                    activateBtn.disabled = true;
                    activateBtn.classList.add("active-preset");
                } else {
                    activateBtn.classList.remove("active-preset");
                    activateBtn.textContent = "Ustaw jako aktywny";
                    activateBtn.disabled = false;
                    activateBtn.onclick = async function () {
                        await deactivateAllPresets();
                        window.presetHasChanged = true;
                        await loadBaseView();
                        await refreshPresetDropdown();
                    };
                }
            }
        }

        await loadCompetitors();
    } catch (err) {
        console.error("loadBaseView error", err);
    } finally {
        hideLoading();
    }
}

async function loadSelectedPreset(presetId) {
    showLoading();
    if (!presetId) {
        const presetSelect = document.getElementById("presetSelect");
        if (presetSelect) presetId = presetSelect.value;
    }
    if (!presetId || presetId === "BASE") {
        await loadBaseView();
        hideLoading();
        return;
    }
    try {
        const resp = await fetch(`/api/Presets/details/${presetId}`);
        const preset = await resp.json();

        window.currentPreset = {
            presetId: preset.presetId,
            storeId: storeId,
            presetName: preset.presetName,
            type: preset.type,
            nowInUse: preset.nowInUse,
            sourceGoogle: preset.sourceGoogle,
            sourceCeneo: preset.sourceCeneo,
            useUnmarkedStores: preset.useUnmarkedStores,
            minDeliveryDays: (preset.minDeliveryDays !== undefined) ? preset.minDeliveryDays : 0,
            maxDeliveryDays: (preset.maxDeliveryDays !== undefined) ? preset.maxDeliveryDays : 31,
            competitors: (preset.competitorItems || []).map(ci => ({
                storeName: ci.storeName,
                dataSource: ci.dataSource,
                useCompetitor: ci.useCompetitor
            }))
        };

        const newPresetSection = document.getElementById("newPresetSection");
        if (newPresetSection) newPresetSection.style.display = "block";

        const presetNameInput = document.getElementById("presetNameInput");
        if (presetNameInput) {
            presetNameInput.style.display = "block";
            presetNameInput.value = preset.presetName || "";
            presetNameInput.disabled = false;
        }

        // --- OBSŁUGA SUWAKA ---
        const deliverySection = document.getElementById("deliveryFilterSection");
        const sliderMin = document.getElementById("deliverySliderMin");
        const sliderMax = document.getElementById("deliverySliderMax");

        if (deliverySection) {
            if (presetTypeContext === 1) { // 1 = Marketplace (Allegro)
                deliverySection.style.display = "flex";
                if (sliderMin && sliderMax) {
                    sliderMin.value = window.currentPreset.minDeliveryDays;
                    sliderMax.value = window.currentPreset.maxDeliveryDays;

                    // ODBLOKOWANIE I PODPIĘCIE EVENTÓW
                    sliderMin.disabled = false;
                    sliderMax.disabled = false;
                    deliverySection.classList.remove("disabled-section");

                    sliderMin.oninput = updateDualSlider;
                    sliderMax.oninput = updateDualSlider;
                    sliderMin.onchange = saveOrUpdatePreset;
                    sliderMax.onchange = saveOrUpdatePreset;

                    updateDualSlider();
                }
            } else {
                deliverySection.style.display = "none";
            }
        }
        // ----------------------

        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            const container = document.getElementById("activateButtonContainer");
            if (container) {
                activateBtn = document.createElement("button");
                activateBtn.id = "activatePresetBtn";
                container.appendChild(activateBtn);
            }
        }

        if (activateBtn) {
            activateBtn.className = "Button-Page-Small";
            if (window.presetContext === 'automationRule') {
                const inputEl = document.getElementById('CompetitorPresetId');
                const currentSelectedId = inputEl ? inputEl.value : "";
                const isSelected = (currentSelectedId !== "" && currentSelectedId == window.currentPreset.presetId);

                if (isSelected) {
                    activateBtn.textContent = "Wybrany";
                    activateBtn.disabled = true;
                    activateBtn.classList.add("active-preset");
                    activateBtn.onclick = null;
                } else {
                    activateBtn.textContent = "Wybierz ten preset";
                    activateBtn.disabled = false;
                    activateBtn.classList.remove("active-preset");
                    activateBtn.onclick = function () {
                        if (typeof window.selectPresetForAutomation === 'function') {
                            window.selectPresetForAutomation(window.currentPreset.presetId, window.currentPreset.presetName);
                        }
                    };
                }
            } else {
                if (window.currentPreset.nowInUse) {
                    activateBtn.textContent = "Aktywny";
                    activateBtn.disabled = true;
                    activateBtn.classList.add("active-preset");
                } else {
                    activateBtn.textContent = "Ustaw jako aktywny";
                    activateBtn.disabled = false;
                    activateBtn.classList.remove("active-preset");
                    activateBtn.onclick = function () {
                        window.currentPreset.nowInUse = true;
                        saveOrUpdatePreset().then(() => {
                            refreshPresetDropdown();
                        });
                        activateBtn.textContent = "Aktywny";
                        activateBtn.disabled = true;
                        activateBtn.classList.add("active-preset");
                    };
                }
            }
        }

        let editBtn = document.getElementById("editPresetBtn");
        const editContainer = document.getElementById("editButtonContainer");
        if (editContainer && !editBtn) {
            editBtn = document.createElement("button");
            editBtn.id = "editPresetBtn";
            editContainer.appendChild(editBtn);
        }
        if (editBtn) {
            editBtn.innerHTML = '<i class="fas fa-pen" style="color:#4e4e4e; font-size:16px;"></i>';
            editBtn.title = "Zmień nazwę presetu";
            editBtn.style.border = "none";
            editBtn.style.borderRadius = "4px";
            editBtn.style.width = "33px";
            editBtn.style.height = "33px";
            editBtn.style.background = "#e3e3e3";
            editBtn.style.cursor = "pointer";
            editBtn.style.display = "inline-block";
            editBtn.onclick = async function () {
                if (!window.currentPreset || !window.currentPreset.presetId) {
                    alert("Nie można zmienić nazwy presetu.");
                    return;
                }
                let newName = prompt("Podaj nową nazwę presetu (max 50 znaków):", window.currentPreset.presetName);
                if (newName && newName.trim() !== "") {
                    newName = newName.trim();
                    if (newName.length > 50) {
                        newName = newName.substring(0, 50);
                        alert("Nazwa została przycięta do 50 znaków.");
                    }
                    window.currentPreset.presetName = newName;
                    await saveOrUpdatePreset();
                    await refreshPresetDropdown();
                    alert("Zmieniono nazwę presetu.");
                }
            };
        }

        let deleteBtn = document.getElementById("deletePresetBtn");
        const deleteContainer = document.getElementById("deleteButtonContainer");
        if (deleteContainer && !deleteBtn) {
            deleteBtn = document.createElement("button");
            deleteBtn.id = "deletePresetBtn";
            deleteContainer.appendChild(deleteBtn);
        }
        if (deleteBtn) {
            deleteBtn.innerHTML = '<i class="fa fa-trash" style="color:red; font-size:20px;"></i>';
            deleteBtn.title = "Usuń preset";
            deleteBtn.style.border = "none";
            deleteBtn.style.borderRadius = "4px";
            deleteBtn.style.width = "33px";
            deleteBtn.style.height = "33px";
            deleteBtn.style.background = "#e3e3e3";
            deleteBtn.style.cursor = "pointer";
            deleteBtn.style.display = "inline-block";

            deleteBtn.onclick = async function () {
                if (confirm("Czy na pewno chcesz usunąć ten preset?")) {
                    try {
                        const deletedPresetId = window.currentPreset.presetId;
                        const resp = await fetch(`/api/Presets/delete/${deletedPresetId}`, {
                            method: "POST",
                            headers: { 'Content-Type': 'application/json' }
                        });

                        if (!resp.ok) {
                            alert("Błąd serwera podczas usuwania presetu.");
                            return;
                        }

                        const data = await resp.json();
                        if (data.success) {
                            alert("Preset został usunięty.");
                            window.presetHasChanged = true;

                            if (window.presetContext === 'automationRule') {
                                const hiddenInput = document.getElementById('CompetitorPresetId');
                                if (hiddenInput && hiddenInput.value == deletedPresetId) {
                                    hiddenInput.value = '';
                                    const labelName = document.getElementById('selectedPresetName');
                                    if (labelName) labelName.innerText = "Domyślny (Wszystkie sklepy)";
                                }
                            }

                            await refreshPresetDropdown();
                            document.getElementById("presetSelect").value = "BASE";
                            await loadBaseView();
                        } else {
                            alert("Błąd: " + data.message);
                        }
                    } catch (err) {
                        console.error("deletePreset error", err);
                    }
                }
            };
        }

        if (presetTypeContext === 0) {
            const googleChk = document.getElementById("googleCheckbox");
            if (googleChk) {
                googleChk.checked = !!preset.sourceGoogle;
                googleChk.disabled = false;
            }
            const ceneoChk = document.getElementById("ceneoCheckbox");
            if (ceneoChk) {
                ceneoChk.checked = !!preset.sourceCeneo;
                ceneoChk.disabled = false;
            }
        }

        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        if (useUnmarkedStoresCheckbox) {
            useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
            useUnmarkedStoresCheckbox.disabled = false;
        }

        await loadCompetitors();

    } catch (err) {
        console.error("loadSelectedPreset error", err);
    } finally {
        hideLoading();
    }
}

function determineSourceVal(isGoogle, isCeneo) {
    if (isGoogle && isCeneo) return "All";
    if (isGoogle) return "Google";
    if (isCeneo) return "Ceneo";
    return "All";
}

async function loadCompetitors() {
    try {
        let url = '';
        if (presetTypeContext === 0) {
            const sourceVal = determineSourceVal(
                document.getElementById("googleCheckbox").checked,
                document.getElementById("ceneoCheckbox").checked
            );
            url = `/api/Presets/competitor-data/${storeId}?ourSource=${sourceVal}`;
        } else if (presetTypeContext === 1) {
            url = `/api/Presets/allegro-competitors/${storeId}`;
        } else {
            return;
        }

        const response = await fetch(url);
        const data = await response.json();

        if (!data.data) {
            console.warn("Brak danych konkurentów");
            return;
        }

        if (presetTypeContext === 0) {
            const googleData = data.data.filter(x => x.dataSource === "Google");
            const ceneoData = data.data.filter(x => x.dataSource === "Ceneo");

            document.getElementById("googleCompetitorsTableBody").innerHTML = "";
            document.getElementById("ceneoCompetitorsTableBody").innerHTML = "";
            googleData.forEach(i => document.getElementById("googleCompetitorsTableBody").appendChild(createRow(i)));
            ceneoData.forEach(i => document.getElementById("ceneoCompetitorsTableBody").appendChild(createRow(i)));

        } else if (presetTypeContext === 1) {

            const allegroBody = document.getElementById("allegroCompetitorsTableBody");
            if (allegroBody) {
                allegroBody.innerHTML = "";
                data.data.forEach(i => allegroBody.appendChild(createRow(i)));
            }
        }

        markPresetCompetitors();

    } catch (err) {
        console.error("Błąd w loadCompetitors:", err);
    }
}

function createRow(item) {
    const tr = document.createElement("tr");
    tr.dataset.originalStoreName = item.storeName;
    tr.dataset.storeName = item.storeName;
    tr.dataset.dataSource = item.dataSource;

    const tdStore = document.createElement("td");
    tdStore.textContent = item.storeName;
    tr.appendChild(tdStore);

    const tdCommon = document.createElement("td");
    tdCommon.textContent = item.commonProductsCount;
    tr.appendChild(tdCommon);

    let sourceActive = true;
    if (presetTypeContext === 0) {
        if (item.dataSource === "Google" && !window.currentPreset.sourceGoogle) {
            sourceActive = false;
        }
        if (item.dataSource === "Ceneo" && !window.currentPreset.sourceCeneo) {
            sourceActive = false;
        }
    }

    const tdAction = document.createElement("td");
    if (!sourceActive) {
        tr.style.backgroundColor = "#cccccc";
        tdAction.textContent = "Niedostępne";
    } else {

        const isGoogleForButtons = item.dataSource === 'Google';

        const addBtn = document.createElement("button");
        addBtn.className = "filterCompetitors-true";
        addBtn.style.marginRight = "5px";
        addBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, isGoogleForButtons, true);
        });
        addBtn.innerHTML = '<i class="fas fa-check"></i>';
        tdAction.appendChild(addBtn);

        const remBtn = document.createElement("button");
        remBtn.className = "filterCompetitors-false";
        remBtn.style.marginRight = "5px";
        remBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, isGoogleForButtons, false);
        });
        remBtn.innerHTML = '<i class="fas fa-times"></i>';
        tdAction.appendChild(remBtn);

        const clearBtn = document.createElement("button");
        clearBtn.className = "filterCompetitors-back";
        clearBtn.addEventListener("click", () => {
            clearCompetitorUsage(item.storeName, isGoogleForButtons);
        });
        clearBtn.innerHTML = '<i class="fas fa-undo"></i>';
        tdAction.appendChild(clearBtn);
    }
    tr.appendChild(tdAction);
    return tr;
}


function updateDualSlider() {
    // Pobieramy elementy "na świeżo"
    const sliderMin = document.getElementById("deliverySliderMin");
    const sliderMax = document.getElementById("deliverySliderMax");
    const sliderRangeFill = document.getElementById("sliderRangeFill");
    const valMinDisplay = document.getElementById("rangeMinVal");
    const valMaxDisplay = document.getElementById("rangeMaxVal");

    if (!sliderMin || !sliderMax) return;

    let slide1 = parseInt(sliderMin.value);
    let slide2 = parseInt(sliderMax.value);

    // Zabezpieczenie przed przekroczeniem
    if (slide1 > slide2) {
        let tmp = slide1;
        sliderMin.value = slide2;
        sliderMax.value = tmp;
        slide1 = slide2;
        slide2 = tmp;
    }

    // Aktualizacja tekstów
    if (valMinDisplay) valMinDisplay.textContent = slide1 === 0 ? "Natychmiast" : slide1;
    if (valMaxDisplay) valMaxDisplay.textContent = slide2 >= 31 ? "31+" : slide2;

    // Aktualizacja paska koloru (fill)
    const percent1 = (slide1 / sliderMin.max) * 100;
    const percent2 = (slide2 / sliderMax.max) * 100;

    if (sliderRangeFill) {
        sliderRangeFill.style.left = percent1 + "%";
        sliderRangeFill.style.width = (percent2 - percent1) + "%";

        // Dodatkowa logika wizualna dla disabled
        if (sliderMin.disabled) {
            sliderRangeFill.style.backgroundColor = "#ccc"; // Szary, gdy zablokowany
        } else {
            sliderRangeFill.style.backgroundColor = "#5a5c69"; // Twój kolor aktywny
        }
    }

    // Aktualizacja obiektu currentPreset (jeśli istnieje)
    if (window.currentPreset) {
        window.currentPreset.minDeliveryDays = slide1;
        window.currentPreset.maxDeliveryDays = slide2;
    }
}

function toggleCompetitorUsage(storeName, isGoogle, useCompetitor) {
    if (!window.currentPreset || !window.currentPreset.presetId) {
        alert("Stwórz własny preset, aby wprowadzać zmiany.");
        return;
    }

    let dataSourceValue;
    if (presetTypeContext === 0) {
        dataSourceValue = isGoogle ? 0 : 1;
    } else {
        dataSourceValue = 2;
    }

    let comp = window.currentPreset.competitors.find(c =>
        c.storeName.toLowerCase() === storeName.toLowerCase() && c.dataSource === dataSourceValue
    );

    if (!comp) {
        comp = { storeName, dataSource: dataSourceValue, useCompetitor };
        window.currentPreset.competitors.push(comp);
    } else {
        comp.useCompetitor = useCompetitor;
    }

    saveOrUpdatePreset();
    refreshRowColor(storeName, isGoogle);
}

function clearCompetitorUsage(storeName, isGoogle) {
    if (!window.currentPreset || !window.currentPreset.presetId) {
        alert("Stwórz własny preset, aby wprowadzać zmiany.");
        return;
    }

    let dataSourceValue;
    if (presetTypeContext === 0) {
        dataSourceValue = isGoogle ? 0 : 1;
    } else {
        dataSourceValue = 2;
    }

    const idx = window.currentPreset.competitors.findIndex(c =>
        c.storeName.toLowerCase() === storeName.toLowerCase() && c.dataSource === dataSourceValue
    );

    if (idx !== -1) {
        window.currentPreset.competitors.splice(idx, 1);
    }

    saveOrUpdatePreset();
    refreshRowColor(storeName, isGoogle);
}

function markPresetCompetitors() {
    if (presetTypeContext === 0) {
        document.querySelectorAll("#googleCompetitorsTableBody tr").forEach(tr => {
            refreshRowColor(tr.dataset.storeName, true);
        });
        document.querySelectorAll("#ceneoCompetitorsTableBody tr").forEach(tr => {
            refreshRowColor(tr.dataset.storeName, false);
        });
    } else if (presetTypeContext === 1) {
        document.querySelectorAll("#allegroCompetitorsTableBody tr").forEach(tr => {
            refreshRowColor(tr.dataset.storeName, null);
        });
    }
}

function refreshRowColor(storeName, isGoogle) {
    let row;
    let dataSourceValue;

    if (presetTypeContext === 0) {
        const ds = isGoogle ? "Google" : "Ceneo";
        row = document.querySelector(`tr[data-store-name="${storeName}"][data-data-source="${ds}"]`);
        dataSourceValue = isGoogle ? 0 : 1;
    } else if (presetTypeContext === 1) {
        row = document.querySelector(`tr[data-store-name="${storeName}"][data-data-source="Allegro"]`);
        dataSourceValue = 2;
    }

    if (!row || !window.currentPreset) return;

    if (presetTypeContext === 0) {
        let sourceActive = true;
        if (isGoogle && !window.currentPreset.sourceGoogle) sourceActive = false;
        if (!isGoogle && !window.currentPreset.sourceCeneo) sourceActive = false;
        if (!sourceActive) {

            row.style.backgroundColor = "#cccccc";
            return;
        }
    }

    const item = window.currentPreset.competitors.find(ci =>
        ci.storeName.toLowerCase() === storeName.toLowerCase() && ci.dataSource === dataSourceValue
    );

    const useUnmarked = window.currentPreset.useUnmarkedStores;
    if (item) {
        row.style.backgroundColor = item.useCompetitor ? "#7AD37A" : "#FC8686";
    } else {
        row.style.backgroundColor = useUnmarked ? "#D8FED8" : "#FFDDDD";
    }
}

document.addEventListener("click", async function (event) {

    if (event.target && event.target.id === "addNewPresetBtn") {
        let presetName = prompt("Podaj nazwę nowego presetu (max 50 znaków):", "");
        if (!presetName) return;

        presetName = presetName.trim();
        if (presetName.length > 50) {
            presetName = presetName.substring(0, 50);
            alert("Nazwa została przycięta do 50 znaków.");
        }

        const newPreset = {
            presetId: 0,
            storeId: storeId,
            presetName,
            type: presetTypeContext,
            nowInUse: false,
            sourceGoogle: true,
            sourceCeneo: true,
            useUnmarkedStores: true,
            competitors: []
        };

        try {
            const resp = await fetch("/api/Presets/save", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(newPreset)
            });
            const data = await resp.json();
            if (data.success) {
                alert(`Utworzono nowy preset`);
                await refreshPresetDropdown();

                const presetSelect = document.getElementById("presetSelect");
                if (presetSelect) presetSelect.value = data.presetId;

                await loadSelectedPreset(data.presetId);
            } else {
                alert("Błąd tworzenia nowego presetu");
            }
        } catch (err) {
            console.error("addNewPreset error:", err);
        }
    }
});

document.addEventListener("DOMContentLoaded", () => {
    const editBtn = document.getElementById("editPresetBtn");
    if (editBtn) {
        editBtn.addEventListener("click", async function () {
            if (!window.currentPreset || !window.currentPreset.presetId) {
                alert("Nie można zmienić nazwy presetu.");
                return;
            }
            let presetName = prompt("Podaj nazwę nowego presetu (max 50 znaków):", window.currentPreset.presetName);
            if (!presetName) return;
            presetName = presetName.trim();
            if (presetName.length > 50) {
                presetName = presetName.substring(0, 50);
                alert("Nazwa została przycięta do 50 znaków.");
            }
            window.currentPreset.presetName = presetName;
            await saveOrUpdatePreset();
            await refreshPresetDropdown();
            alert("Zmieniono nazwę presetu.");
        });
    }
});

document.addEventListener("DOMContentLoaded", () => {
    const googleChk = document.getElementById("googleCheckbox");
    const ceneoChk = document.getElementById("ceneoCheckbox");
    if (googleChk && ceneoChk) {
        async function onSourceChange() {

            if (!window.currentPreset || window.currentPreset.presetId === null) {
                const val = determineSourceVal(googleChk.checked, ceneoChk.checked);
                showLoading();
                await loadCompetitors(val);
                hideLoading();
                return;
            }

            window.currentPreset.sourceGoogle = googleChk.checked;
            window.currentPreset.sourceCeneo = ceneoChk.checked;
            showLoading();
            await saveOrUpdatePreset();
            const sourceVal = determineSourceVal(googleChk.checked, ceneoChk.checked);
            await loadCompetitors(sourceVal);
            hideLoading();
        }
        googleChk.addEventListener("change", onSourceChange);
        ceneoChk.addEventListener("change", onSourceChange);
    }
});

async function saveOrUpdatePreset() {
    if (!window.currentPreset) return;
    if (!window.currentPreset.presetId) return;
    try {
        const resp = await fetch("/api/Presets/save", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(window.currentPreset)
        });
        const data = await resp.json();
        if (!data.success) {
            alert("Błąd zapisu presetów: " + (data.message || ""));
            return;
        }
        window.currentPreset.presetId = data.presetId;
        window.presetHasChanged = true;
    } catch (err) {
        console.error("saveOrUpdatePreset error", err);
        alert("Błąd zapisu (SaveOrUpdatePreset). Sprawdź konsolę.");
    }
}

function filterCompetitors(searchTerm) {
    let selector;

    if (presetTypeContext === 0) {
        selector = "#googleCompetitorsTableBody tr, #ceneoCompetitorsTableBody tr";
    } else if (presetTypeContext === 1) {
        selector = "#allegroCompetitorsTableBody tr";
    } else {

        return;
    }

    const allRows = document.querySelectorAll(selector);

    allRows.forEach(tr => {
        const originalStoreName = tr.dataset.originalStoreName || "";
        const storeNameLower = originalStoreName.toLowerCase();
        const searchLower = searchTerm.toLowerCase();

        const storeTd = tr.querySelector("td:nth-child(1)");
        if (!storeTd) return;

        if (!searchTerm || storeNameLower.includes(searchLower)) {
            tr.style.display = "";
            const highlighted = highlightTextCaseInsensitive(originalStoreName, searchTerm);
            storeTd.innerHTML = highlighted;
        } else {
            tr.style.display = "none";
            storeTd.textContent = originalStoreName;
        }
    });
}

function highlightTextCaseInsensitive(fullText, searchTerm) {
    if (!searchTerm) return fullText;
    const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const regex = new RegExp(`(${escapedTerm})`, 'gi');
    return fullText.replace(regex, '<span style="font-weight:bold; color:purple;">$1</span>');
}

document.addEventListener('keydown', function (event) {
    if (event.key === "Escape") {
        const modal = document.getElementById('competitorModal');

        if (modal && modal.classList.contains('show')) {
            modal.style.display = 'none';
            modal.classList.remove('show');

            if (typeof priceHistoryPageContext !== 'undefined') {
                if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {

                    loadPrices();
                } else if (priceHistoryPageContext === 'details') {

                    window.location.reload();
                }
            } else {

            }

        }
    }
});

document.addEventListener("click", function (event) {

    const closeBtn = event.target.closest("[data-dismiss='modal']");

    if (closeBtn) {

        const modal = closeBtn.closest(".modal-sim") || document.getElementById("competitorModal");

        if (modal) {

            modal.style.display = "none";
            modal.classList.remove("show");

            const backdrops = document.getElementsByClassName('modal-backdrop');
            while (backdrops.length > 0) {
                backdrops[0].parentNode.removeChild(backdrops[0]);
            }
            document.body.classList.remove('modal-open');

            hideLoading();

            if (modal.id === "competitorModal") {
                if (window.presetHasChanged === true) {
                    console.log("Zmiany w presetach wykryte - odświeżam widok.");

                    if (typeof priceHistoryPageContext !== 'undefined') {
                        if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
                            loadPrices();

                        } else if (priceHistoryPageContext === 'details') {
                            window.location.reload();

                        }
                    }
                } else {
                    console.log("Brak zmian w presetach - nie odświeżam.");
                }
            }

            else {
                if (typeof priceHistoryPageContext !== 'undefined') {
                    if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
                        loadPrices();
                    } else if (priceHistoryPageContext === 'details') {
                        window.location.reload();
                    }
                }
            }
        }
    }
});

document.addEventListener('keydown', function (event) {
    if (event.key === "Escape") {
        const modal = document.querySelector('.modal-sim.show') || document.getElementById('competitorModal');
        if (modal && (modal.style.display === 'block' || modal.classList.contains('show'))) {
            modal.style.display = 'none';
            modal.classList.remove('show');
            document.body.classList.remove('modal-open');
        }
    }
});