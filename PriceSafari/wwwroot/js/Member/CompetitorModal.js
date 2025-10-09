window.currentPreset = null;

function showLoading() {
    document.getElementById("loadingOverlay").style.display = "flex";
}

function hideLoading() {
    document.getElementById("loadingOverlay").style.display = "none";
}

window.openCompetitorsModal = async function () {
    await refreshPresetDropdown();
    const presetSelect = document.getElementById("presetSelect");

    const activeOption = Array.from(presetSelect.options).find(opt =>
        opt.value !== "BASE" && opt.textContent.includes("[aktywny]")
    );

    if (activeOption) {
        presetSelect.value = activeOption.value;
        await loadSelectedPreset(activeOption.value);
    } else {
        presetSelect.value = "BASE";
        loadBaseView();
    }

    const competitorModal = document.getElementById("competitorModal");
    competitorModal.style.display = 'block';
    competitorModal.classList.add('show');
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
        opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
        presetSelect.appendChild(opt);
    });

    if (window.currentPreset && window.currentPreset.presetId) {
        presetSelect.value = window.currentPreset.presetId;
    } else {
        presetSelect.value = "BASE";
    }
}

document.addEventListener("DOMContentLoaded", () => {
    const presetSelect = document.getElementById("presetSelect");
    if (presetSelect) {
        presetSelect.addEventListener("change", async function () {
            if (this.value === "BASE") {
                loadBaseView();
            } else {
                await loadSelectedPreset(this.value);
            }
        });
    }

    const searchInput = document.getElementById("competitorSearchInput");
    if (searchInput) {
        searchInput.addEventListener("input", function () {
            filterCompetitors(this.value);
        });
    }

    const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
    if (useUnmarkedStoresCheckbox) {
        useUnmarkedStoresCheckbox.addEventListener("change", async function () {

            if (!window.currentPreset || window.currentPreset.presetId === null) {
                if (window.currentPreset) {
                    window.currentPreset.useUnmarkedStores = this.checked;
                    showLoading();
                    markPresetCompetitors();
                    hideLoading();
                }
                return;
            }

            window.currentPreset.useUnmarkedStores = this.checked;
            showLoading();
            await saveOrUpdatePreset();
            markPresetCompetitors();
            hideLoading();
        });
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
            competitors: []
        };

        const newPresetSection = document.getElementById("newPresetSection");
        newPresetSection.style.display = "block";

        const presetNameInput = document.getElementById("presetNameInput");
        if (presetNameInput) {
            presetNameInput.style.display = "none";
        }
        const editBtn = document.getElementById("editPresetBtn");
        if (editBtn) {
            editBtn.style.display = "none";
        }
        const deleteBtn = document.getElementById("deletePresetBtn");
        if (deleteBtn) {
            deleteBtn.style.display = "none";
        }

        const googleCheckbox = document.getElementById("googleCheckbox");
        googleCheckbox.checked = window.currentPreset.sourceGoogle;
        googleCheckbox.disabled = true;
        const ceneoCheckbox = document.getElementById("ceneoCheckbox");
        ceneoCheckbox.checked = window.currentPreset.sourceCeneo;
        ceneoCheckbox.disabled = true;
        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
        useUnmarkedStoresCheckbox.disabled = true;

        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            activateBtn = document.createElement("button");
            activateBtn.id = "activatePresetBtn";
            document.getElementById("activateButtonContainer").appendChild(activateBtn);
        }

        activateBtn.className = "Button-Page-Small";

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
                await loadBaseView();
                await refreshPresetDropdown();
            };
        }

        await loadCompetitors("All");
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
            competitors: (preset.competitorItems || []).map(ci => ({
                storeName: ci.storeName,

                dataSource: ci.dataSource,
                useCompetitor: ci.useCompetitor
            }))
        };

        document.getElementById("newPresetSection").style.display = "block";

        const presetNameInput = document.getElementById("presetNameInput");
        if (presetNameInput) {
            presetNameInput.style.display = "block";
            presetNameInput.value = preset.presetName || "";
            presetNameInput.disabled = false;
        }

        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            activateBtn = document.createElement("button");
            activateBtn.id = "activatePresetBtn";
            document.getElementById("activateButtonContainer").appendChild(activateBtn);
        }

        activateBtn.className = "Button-Page-Small";

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

        let editBtn = document.getElementById("editPresetBtn");
        if (editBtn) {
            if (editBtn.parentElement.id !== "editButtonContainer") {
                editBtn.parentElement.removeChild(editBtn);
                document.getElementById("editButtonContainer").appendChild(editBtn);
            }
            editBtn.innerHTML = '<i class="fas fa-pen" style="color:#4e4e4e; font-size:16px;"></i>';
            editBtn.title = "Zmień nazwę presetu";
            editBtn.style.border = "none";
            editBtn.style.borderRadius = "4px";
            editBtn.style.width = "33px";
            editBtn.style.height = "33px";
            editBtn.style.background = "#e3e3e3";
            editBtn.style.cursor = "pointer";
        } else {
            editBtn = document.createElement("button");
            editBtn.id = "editPresetBtn";
            editBtn.innerHTML = '<i class="fas fa-pen" style="color:#4e4e4e; font-size:16px;"></i>';
            editBtn.title = "Zmień nazwę presetu";
            editBtn.style.border = "none";
            editBtn.style.borderRadius = "4px";
            editBtn.style.width = "33px";
            editBtn.style.height = "33px";
            editBtn.style.background = "#e3e3e3";
            editBtn.style.cursor = "pointer";
            document.getElementById("editButtonContainer").appendChild(editBtn);
        }

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

        let deleteBtn = document.getElementById("deletePresetBtn");
        if (!deleteBtn) {
            deleteBtn = document.createElement("button");
            deleteBtn.id = "deletePresetBtn";
            deleteBtn.innerHTML = '<i class="fa fa-trash" style="color:red; font-size:20px;"></i>';
            deleteBtn.title = "Usuń preset";
            deleteBtn.style.border = "none";
            deleteBtn.style.borderRadius = "4px";
            deleteBtn.style.width = "33px";
            deleteBtn.style.height = "33px";
            deleteBtn.style.background = "#e3e3e3";
            deleteBtn.style.cursor = "pointer";
            document.getElementById("deleteButtonContainer").appendChild(deleteBtn);
        }

        deleteBtn.style.display = "inline-block";
        deleteBtn.onclick = async function () {
            if (confirm("Czy na pewno chcesz usunąć ten preset?")) {
                try {

                    const resp = await fetch(`/api/Presets/delete/${window.currentPreset.presetId}`, {
                        method: "POST",
                        headers: { 'Content-Type': 'application/json' }

                    });

                    if (!resp.ok) {
                        const errorText = await resp.text();
                        console.error("Błąd usuwania:", errorText);
                        alert("Błąd serwera podczas usuwania presetu.");
                        return;
                    }

                    const data = await resp.json();
                    if (data.success) {
                        alert("Preset został usunięty.");
                        await refreshPresetDropdown();
                        document.getElementById("presetSelect").value = "BASE";
                        await loadBaseView();
                    } else {
                        alert("Błąd usuwania presetu: " + (data.message || ""));
                    }
                } catch (err) {
                    console.error("deletePreset error", err);
                }
            }
        };

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
        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
        useUnmarkedStoresCheckbox.disabled = false;

        const sourceVal = determineSourceVal(!!preset.sourceGoogle, !!preset.sourceCeneo);
        await loadCompetitors(sourceVal);
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

// W pliku: CompetitorModal.js

async function loadCompetitors() { // Usunęliśmy argument 'ourSource', nie jest już potrzebny
    try {
        let url = '';
        if (presetTypeContext === 0) { // 0 = PriceComparison
            // Dla porównywarek używamy starego endpointu
            const sourceVal = determineSourceVal(
                document.getElementById("googleCheckbox").checked,
                document.getElementById("ceneoCheckbox").checked
            );
            url = `/api/Presets/competitor-data/${storeId}?ourSource=${sourceVal}`;
        } else if (presetTypeContext === 1) { // 1 = Marketplace (Allegro)
            // Dla Allegro używamy nowego endpointu
            url = `/api/Presets/allegro-competitors/${storeId}`;
        } else {
            return; // Nieznany kontekst
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
            // TUTAJ MUSISZ MIEĆ OSOBNĄ TABELĘ W HTML-u DLA ALLEGRO
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

    const tdStore = document.createElement("td");
    tdStore.textContent = item.storeName;
    tr.appendChild(tdStore);

    const tdCommon = document.createElement("td");
    tdCommon.textContent = item.commonProductsCount;
    tr.appendChild(tdCommon);

    let sourceActive = true;
    if (item.dataSource === "Google" && !window.currentPreset.sourceGoogle) {
        sourceActive = false;
    }
    if (item.dataSource === "Ceneo" && !window.currentPreset.sourceCeneo) {
        sourceActive = false;
    }

    const tdAction = document.createElement("td");
    if (!sourceActive) {
        tr.style.backgroundColor = "#cccccc";
        tdAction.textContent = "Niedostępne";
    } else {
        const addBtn = document.createElement("button");
        addBtn.classList.add("filterCompetitors-true");
        addBtn.style.marginRight = "5px";
        addBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), true);
        });
        addBtn.innerHTML = '<i class="fas fa-check"></i>';
        tdAction.appendChild(addBtn);

        const remBtn = document.createElement("button");
        remBtn.classList.add("filterCompetitors-false");
        remBtn.style.marginRight = "5px";
        remBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), false);
        });
        remBtn.innerHTML = '<i class="fas fa-times"></i>';
        tdAction.appendChild(remBtn);

        const clearBtn = document.createElement("button");
        clearBtn.classList.add("filterCompetitors-back");
        clearBtn.addEventListener("click", () => {
            clearCompetitorUsage(item.storeName, (item.dataSource === "Google"));
        });
        clearBtn.innerHTML = '<i class="fas fa-undo"></i>';
        tdAction.appendChild(clearBtn);
    }

    tr.appendChild(tdAction);

    tr.dataset.storeName = item.storeName;
    tr.dataset.dataSource = item.dataSource;

    return tr;
}

function toggleCompetitorUsage(storeName, isGoogle, useCompetitor) {
    if (!window.currentPreset || !window.currentPreset.presetId) {
        alert("Stwórz własny preset, aby wprowadzać zmiany.");
        return;
    }

    const dataSourceValue = isGoogle ? 0 : 1;

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
    const dataSourceValue = isGoogle ? 0 : 1;
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
    document.querySelectorAll("#googleCompetitorsTableBody tr").forEach(tr => {
        refreshRowColor(tr.dataset.storeName, true);
    });
    document.querySelectorAll("#ceneoCompetitorsTableBody tr").forEach(tr => {
        refreshRowColor(tr.dataset.storeName, false);
    });
}

function refreshRowColor(storeName, isGoogle) {
    const ds = isGoogle ? "Google" : "Ceneo";
    const row = document.querySelector(`tr[data-store-name="${storeName}"][data-data-source="${ds}"]`);
    if (!row || !window.currentPreset) return;
    let sourceActive = true;
    if (ds === "Google" && !window.currentPreset.sourceGoogle) sourceActive = false;
    if (ds === "Ceneo" && !window.currentPreset.sourceCeneo) sourceActive = false;
    if (!sourceActive) return;
    const dataSourceValue = isGoogle ? 0 : 1;
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

document.addEventListener("DOMContentLoaded", () => {
    const btnNew = document.getElementById("addNewPresetBtn");
    if (!btnNew) return;
    btnNew.addEventListener("click", async function () {
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
                document.getElementById("presetSelect").value = data.presetId;
                await loadSelectedPreset(data.presetId);
            } else {
                alert("Błąd tworzenia nowego presetu");
            }
        } catch (err) {
            console.error("addNewPreset error:", err);
        }
    });
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
    } catch (err) {
        console.error("saveOrUpdatePreset error", err);
        alert("Błąd zapisu (SaveOrUpdatePreset). Sprawdź konsolę.");
    }
}

function filterCompetitors(searchTerm) {
    const allRows = document.querySelectorAll("#googleCompetitorsTableBody tr, #ceneoCompetitorsTableBody tr");

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

document.addEventListener('click', function (event) {
    const modal = document.getElementById('competitorModal');

    if (modal && modal.classList.contains('show')) {
        const dialog = modal.querySelector('.modal-simulation');

        if (dialog && !dialog.contains(event.target)) {
            modal.style.display = 'none';
            modal.classList.remove('show');

            if (typeof priceHistoryPageContext !== 'undefined') {
                if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
                    console.log("Closing modal on Index page, calling loadPrices().");
                    loadPrices();
                } else if (priceHistoryPageContext === 'details') {
                    console.log("Closing modal on Details page, reloading.");
                    window.location.reload();
                }
            } else {
                console.warn("priceHistoryPageContext is not defined.");
            }

        }
    }
});

document.addEventListener('keydown', function (event) {
    if (event.key === "Escape") {
        const modal = document.getElementById('competitorModal');

        if (modal && modal.classList.contains('show')) {
            modal.style.display = 'none';
            modal.classList.remove('show');

            if (typeof priceHistoryPageContext !== 'undefined') {
                if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
                    console.log("Closing modal on Index page (ESC), calling loadPrices().");
                    loadPrices();
                } else if (priceHistoryPageContext === 'details') {
                    console.log("Closing modal on Details page (ESC), reloading.");
                    window.location.reload();
                }
            } else {
                console.warn("priceHistoryPageContext is not defined.");
            }

        }
    }
});

document.addEventListener("DOMContentLoaded", function () {

    const competitorModal = document.getElementById("competitorModal");
    if (competitorModal) {
        competitorModal.addEventListener("click", function (event) {
            const closeBtn = event.target.closest("[data-dismiss='modal']");
            if (closeBtn) {
                competitorModal.style.display = "none";
                competitorModal.classList.remove("show");

                if (typeof priceHistoryPageContext !== 'undefined') {
                    if (priceHistoryPageContext === 'index' && typeof loadPrices === 'function') {
                        console.log("Closing modal on Index page (button), calling loadPrices().");
                        loadPrices();
                    } else if (priceHistoryPageContext === 'details') {
                        console.log("Closing modal on Details page (button), reloading.");
                        window.location.reload();
                    }
                } else {
                    console.warn("priceHistoryPageContext is not defined.");
                }

            }
        });
    }

});