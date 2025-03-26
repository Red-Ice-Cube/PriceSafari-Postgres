/*****************************************************
 * CAŁY KOD JAVASCRIPT (z uwzględnionymi zmianami)
 *****************************************************/

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

    // Szukamy aktywnego customowego presetu
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

// Pobiera listę presetów z backendu
async function fetchPresets() {
    const url = `/PriceHistory/GetPresets?storeId=${storeId}`;
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

    // Sprawdzamy, czy istnieje jakikolwiek aktywny customowy preset
    const anyActive = newPresets.some(p => p.nowInUse === true);

    // Dla widoku bazowego w zależności od wyniku powyższego sprawdzenia
    const baseOpt = document.createElement("option");
    baseOpt.value = "BASE";
    if (!anyActive) {
        baseOpt.textContent = "PriceSafari - Wszystkie Dane [aktywny]";
    } else {
        baseOpt.textContent = "PriceSafari - Wszystkie Dane";
    }
    presetSelect.appendChild(baseOpt);

    // Dodajemy pozostałe presety
    newPresets.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p.presetId;
        opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
        presetSelect.appendChild(opt);
    });

    // Ustawiamy wybraną wartość – jeśli currentPreset posiada presetId, ustawiamy go,
    // w przeciwnym razie wybieramy "BASE".
    if (window.currentPreset && window.currentPreset.presetId) {
        presetSelect.value = window.currentPreset.presetId;
    } else {
        presetSelect.value = "BASE";
    }
}

// Ustawienie listenera dla dropdowna (ustawiony raz przy DOMContentLoaded)
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

    const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
    if (useUnmarkedStoresCheckbox) {
        useUnmarkedStoresCheckbox.addEventListener("change", async function () {
            // Dla widoku bazowego – tylko odświeżamy UI
            if (!window.currentPreset || window.currentPreset.presetId === null) {
                if (window.currentPreset) {
                    window.currentPreset.useUnmarkedStores = this.checked;
                    showLoading();
                    markPresetCompetitors();
                    hideLoading();
                }
                return;
            }
            // Dla customowych widoków: aktualizujemy wartość, zapisujemy i odświeżamy kolory
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
        const resp = await fetch(`/PriceHistory/DeactivateAllPresets?storeId=${storeId}`, {
            method: "POST"
        });
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
    showLoading(); // Pokazujemy spinner
    try {
        const customActive = await isAnyCustomPresetActive();

        // Ustawiamy widok bazowy
        window.currentPreset = {
            presetId: null,
            storeId: storeId,
            presetName: "PriceSafari - Wszystkie Dane",
            nowInUse: !customActive, // Bazowy aktywny tylko, gdy nie ma custom presetów
            sourceGoogle: true,
            sourceCeneo: true,
            useUnmarkedStores: true,
            competitors: []
        };

        const newPresetSection = document.getElementById("newPresetSection");
        newPresetSection.style.display = "block";

        // Ukrywamy pola edycji dla widoku bazowego
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

        // Ustawiamy elementy jako read-only
        const googleCheckbox = document.getElementById("googleCheckbox");
        googleCheckbox.checked = window.currentPreset.sourceGoogle;
        googleCheckbox.disabled = true;
        const ceneoCheckbox = document.getElementById("ceneoCheckbox");
        ceneoCheckbox.checked = window.currentPreset.sourceCeneo;
        ceneoCheckbox.disabled = true;
        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
        useUnmarkedStoresCheckbox.disabled = true;

        // Dynamiczny przycisk aktywacji dla widoku bazowego
        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            activateBtn = document.createElement("button");
            activateBtn.id = "activatePresetBtn";
            newPresetSection.appendChild(activateBtn);
        }
        if (window.currentPreset.nowInUse) {
            activateBtn.textContent = "Aktywny";
            activateBtn.disabled = true;
        } else {
            activateBtn.textContent = "Ustaw jako aktywny";
            activateBtn.disabled = false;
            activateBtn.onclick = async function () {
                // Deaktywujemy customowe presety
                await deactivateAllPresets();
                // Ładujemy ponownie widok bazowy (teraz jako aktywny)
                await loadBaseView();
                // Odświeżamy dropdown
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
        const resp = await fetch(`/PriceHistory/GetPresetDetails?presetId=${presetId}`);
        const preset = await resp.json();
        window.currentPreset = {
            presetId: preset.presetId,
            storeId: storeId,
            presetName: preset.presetName,
            nowInUse: preset.nowInUse,
            sourceGoogle: preset.sourceGoogle,
            sourceCeneo: preset.sourceCeneo,
            useUnmarkedStores: preset.useUnmarkedStores,
            competitors: (preset.competitorItems || []).map(ci => ({
                storeName: ci.storeName,
                isGoogle: ci.isGoogle,
                useCompetitor: ci.useCompetitor
            }))
        };

        document.getElementById("newPresetSection").style.display = "block";

        // Ustawianie pól formularza
        const presetNameInput = document.getElementById("presetNameInput");
        if (presetNameInput) {
            presetNameInput.style.display = "block";
            presetNameInput.value = preset.presetName || "";
            presetNameInput.disabled = false;
        }
        const editBtn = document.getElementById("editPresetBtn");
        if (editBtn) {
            editBtn.style.display = "inline-block";
        }

        // Konfiguracja przycisku aktywacji
        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            activateBtn = document.createElement("button");
            activateBtn.id = "activatePresetBtn";
            document.getElementById("newPresetSection").appendChild(activateBtn);
        }
        if (window.currentPreset.nowInUse) {
            activateBtn.textContent = "Aktywny";
            activateBtn.disabled = true;
        } else {
            activateBtn.textContent = "Ustaw jako aktywny";
            activateBtn.disabled = false;
            activateBtn.onclick = function () {
                window.currentPreset.nowInUse = true;
                saveOrUpdatePreset().then(() => refreshPresetDropdown());
                activateBtn.textContent = "Aktywny";
                activateBtn.disabled = true;
            };
        }

        // Konfiguracja przycisku usuwania (dla customowych presetów)
        let deleteBtn = document.getElementById("deletePresetBtn");
        if (!deleteBtn) {
            deleteBtn = document.createElement("button");
            deleteBtn.id = "deletePresetBtn";
            deleteBtn.textContent = "Usuń preset";
            document.getElementById("newPresetSection").appendChild(deleteBtn);
        }
        deleteBtn.style.display = "inline-block";
        deleteBtn.onclick = async function () {
            if (confirm("Czy na pewno chcesz usunąć ten preset?")) {
                try {
                    const resp = await fetch(`/PriceHistory/DeletePreset?presetId=${window.currentPreset.presetId}`, {
                        method: "POST"
                    });
                    const data = await resp.json();
                    if (data.success) {
                        alert("Preset został usunięty.");
                        await refreshPresetDropdown();
                        const presetSelect = document.getElementById("presetSelect");
                        presetSelect.value = "BASE";
                        await loadBaseView();
                    } else {
                        alert("Błąd usuwania presetu: " + (data.message || ""));
                    }
                } catch (err) {
                    console.error("deletePreset error", err);
                }
            }
        };

        // Ustawienie checkboxów
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

async function loadCompetitors(ourSource) {
    try {
        const url = `/PriceHistory/GetCompetitorStoresData?storeId=${storeId}&ourSource=${ourSource}`;
        const response = await fetch(url);
        const data = await response.json();
        if (!data.data) {
            console.warn("Brak data");
            return;
        }
        const googleData = data.data.filter(x => x.dataSource === "Google");
        const ceneoData = data.data.filter(x => x.dataSource === "Ceneo");

        const gBody = document.getElementById("googleCompetitorsTableBody");
        const cBody = document.getElementById("ceneoCompetitorsTableBody");
        if (gBody) gBody.innerHTML = "";
        if (cBody) cBody.innerHTML = "";

        googleData.forEach(i => gBody.appendChild(createRow(i)));
        ceneoData.forEach(i => cBody.appendChild(createRow(i)));

        markPresetCompetitors();
    } catch (err) {
        console.error(err);
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
        // Przycisk "Dodaj"
        const addBtn = document.createElement("button");
        addBtn.textContent = "Dodaj";
        addBtn.style.marginRight = "5px";
        addBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), true);
        });
        tdAction.appendChild(addBtn);

        // Przycisk "Usuń"
        const remBtn = document.createElement("button");
        remBtn.textContent = "Usuń";
        remBtn.style.marginRight = "5px";
        remBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), false);
        });
        tdAction.appendChild(remBtn);

        // ****** NOWY PRZYCISK "Oczyść" (reset) ******
        const clearBtn = document.createElement("button");
        clearBtn.textContent = "Oczyść";
        clearBtn.addEventListener("click", () => {
            clearCompetitorUsage(item.storeName, (item.dataSource === "Google"));
        });
        tdAction.appendChild(clearBtn);
    }
    tr.appendChild(tdAction);

    tr.dataset.storeName = item.storeName;
    tr.dataset.dataSource = item.dataSource;

    return tr;
}

// Dodaje lub usuwa sklep z listy competitorów i zapisuje zmiany
function toggleCompetitorUsage(storeName, isGoogle, useCompetitor) {
    if (!window.currentPreset || !window.currentPreset.presetId) {
        alert("Stwórz własny preset, aby wprowadzać zmiany.");
        return;
    }
    let comp = window.currentPreset.competitors.find(c =>
        c.storeName.toLowerCase() === storeName.toLowerCase() && c.isGoogle === isGoogle
    );
    if (!comp) {
        comp = { storeName, isGoogle, useCompetitor };
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
    const idx = window.currentPreset.competitors.findIndex(c =>
        c.storeName.toLowerCase() === storeName.toLowerCase() && c.isGoogle === isGoogle
    );
    if (idx !== -1) {
        // Usuwamy ten element z listy
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
    const item = window.currentPreset.competitors.find(ci =>
        ci.storeName.toLowerCase() === storeName.toLowerCase() && ci.isGoogle === isGoogle
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
        let presetName = prompt("Podaj nazwę nowego presetu (max 40 znaków):", "");
        if (!presetName) return;
        presetName = presetName.trim();
        if (presetName.length > 40) {
            presetName = presetName.substring(0, 40);
            alert("Nazwa została przycięta do 40 znaków.");
        }
        const newPreset = {
            presetId: 0,
            storeId: storeId,
            presetName,
            nowInUse: false,
            sourceGoogle: true,
            sourceCeneo: true,
            useUnmarkedStores: true,
            competitors: []
        };
        try {
            const resp = await fetch("/PriceHistory/SaveOrUpdatePreset", {
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

// Aktualizacja nazwy presetu (customowych widoków)
document.addEventListener("DOMContentLoaded", () => {
    const nameInput = document.getElementById("presetNameInput");
    const editBtn = document.getElementById("editPresetBtn");
    if (editBtn) {
        editBtn.addEventListener("click", async function () {
            if (!window.currentPreset || !window.currentPreset.presetId) {
                alert("Nie można zmienić nazwy presetu.");
                return;
            }
            window.currentPreset.presetName = nameInput.value.trim();
            await saveOrUpdatePreset();
            await refreshPresetDropdown();
            alert("Zmieniono nazwę presetu.");
        });
    }
});

// Listener dla checkboxów źródeł: Google i Ceneo
document.addEventListener("DOMContentLoaded", () => {
    const googleChk = document.getElementById("googleCheckbox");
    const ceneoChk = document.getElementById("ceneoCheckbox");
    if (googleChk && ceneoChk) {
        async function onSourceChange() {
            // Dla widoku bazowego – tylko zmieniamy dane konkurencji
            if (!window.currentPreset || window.currentPreset.presetId === null) {
                const val = determineSourceVal(googleChk.checked, ceneoChk.checked);
                showLoading();
                await loadCompetitors(val);
                hideLoading();
                return;
            }
            // Dla customowych presetów aktualizujemy właściwości, zapisujemy i ładujemy konkurentów
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

// Zapis lub aktualizacja presetu (tylko dla customowych widoków)
async function saveOrUpdatePreset() {
    if (!window.currentPreset) return;
    if (!window.currentPreset.presetId) return; // widoki bazowe nie są zapisywane
    try {
        const resp = await fetch("/PriceHistory/SaveOrUpdatePreset", {
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

/***********************************************
 * Nowe funkcje do wyszukiwania i podświetlania
 ***********************************************/
function filterCompetitors(searchTerm) {
    const allRows = document.querySelectorAll("#googleCompetitorsTableBody tr, #ceneoCompetitorsTableBody tr");

    allRows.forEach(tr => {
        const originalStoreName = tr.dataset.originalStoreName || "";
        const storeNameLower = originalStoreName.toLowerCase();
        const searchLower = searchTerm.toLowerCase();

        const storeTd = tr.querySelector("td:nth-child(1)");
        if (!storeTd) return; // bezpieczeństwo

        // Sprawdzamy czy nazwa sklepu zawiera wpisany fragment
        if (!searchTerm || storeNameLower.includes(searchLower)) {
            // Pokazujemy wiersz
            tr.style.display = "";

            // Podmieniamy fragment dopasowanego tekstu na pogrubiony fioletowy
            const highlighted = highlightTextCaseInsensitive(originalStoreName, searchTerm);
            storeTd.innerHTML = highlighted;
        } else {
            // Ukrywamy wiersz
            tr.style.display = "none";
            // Można przywrócić oryginalny tekst, ale i tak wiersz jest niewidoczny
            storeTd.textContent = originalStoreName;
        }
    });
}

function highlightTextCaseInsensitive(fullText, searchTerm) {
    if (!searchTerm) return fullText; // Jeśli brak frazy, zwracamy oryginał

    // Ucieczka znaków regex, aby wpis typu ".*" nie sypał nam wyrażeń
    const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

    // Budujemy regex dopasowujący w sposób nieczuły na wielkość liter
    const regex = new RegExp(`(${escapedTerm})`, 'gi');

    // Zamieniamy każdą dopasowaną frazę na <span style="font-weight:bold; color:purple;">$1</span>
    return fullText.replace(regex, '<span style="font-weight:bold; color:purple;">$1</span>');
}

/***********************************************
 * Obsługa kliknięć / zamykania modala
 ***********************************************/
document.addEventListener('click', function (event) {
    const modal = document.getElementById('competitorModal');
    if (modal.classList.contains('show')) {
        const dialog = modal.querySelector('.modal-simulation');
        if (!dialog.contains(event.target)) {
            // Kliknieto w tło - zamykamy
            modal.style.display = 'none';
            modal.classList.remove('show');
            loadPrices(); // Zakładamy, że masz gdzieś zdefiniowane loadPrices()
        }
    }
});

document.addEventListener('keydown', function (event) {
    if (event.key === "Escape") {
        const modal = document.getElementById('competitorModal');
        if (modal.classList.contains('show')) {
            modal.style.display = 'none';
            modal.classList.remove('show');
            loadPrices();
        }
    }
});

document.addEventListener("DOMContentLoaded", function () {
    // Nasłuchujemy kliknięć na całym modalu i sprawdzamy, czy kliknięto element z data-dismiss='modal'
    const competitorModal = document.getElementById("competitorModal");
    competitorModal.addEventListener("click", function (event) {
        const closeBtn = event.target.closest("[data-dismiss='modal']");
        if (closeBtn) {
            competitorModal.style.display = "none";
            competitorModal.classList.remove("show");
            loadPrices();
        }
    });

    // Nasłuch dla inputu wyszukiwania
    const competitorSearchInput = document.getElementById("competitorSearchInput");
    if (competitorSearchInput) {
        competitorSearchInput.addEventListener("input", function () {
            filterCompetitors(this.value);
        });
    }
});
