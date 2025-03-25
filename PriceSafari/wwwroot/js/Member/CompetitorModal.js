// Globalny obiekt widoku
window.currentPreset = null;

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

    $('#competitorModal').modal('show');
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

    // Listener dla checkboxa "używaj nieoznaczonych sklepów"
    const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
    if (useUnmarkedStoresCheckbox) {
        useUnmarkedStoresCheckbox.addEventListener("change", function () {
            // Dla widoku bazowego – tylko odświeżamy UI
            if (!window.currentPreset || window.currentPreset.presetId === null) {
                if (window.currentPreset) {
                    window.currentPreset.useUnmarkedStores = this.checked;
                    markPresetCompetitors();
                }
                return;
            }
            // Dla customowych widoków: aktualizujemy wartość, zapisujemy i odświeżamy kolory
            window.currentPreset.useUnmarkedStores = this.checked;
            saveOrUpdatePreset().then(() => markPresetCompetitors());
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
    // Sprawdzamy, czy istnieje jakikolwiek aktywny customowy preset
    const customActive = await isAnyCustomPresetActive();

    // Jeśli customActive === true, oznacza to, że widok bazowy nie jest aktywny
    window.currentPreset = {
        presetId: null,
        storeId: storeId,
        presetName: "PriceSafari - Wszystkie Dane",
        nowInUse: !customActive, // kluczowa zmiana – bazowy jest aktywny tylko, gdy nie ma aktywnych customów
        sourceGoogle: true,
        sourceCeneo: true,
        useUnmarkedStores: true,
        competitors: []
    };

    const newPresetSection = document.getElementById("newPresetSection");
    newPresetSection.style.display = "block";

    // Ukrywamy pole nazwy oraz przycisk edycji dla widoku bazowego
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

    // Ustawiamy pozostałe elementy jako read-only
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

    // Jeśli widok bazowy jest aktywny (nowInUse: true), blokujemy przycisk i wyświetlamy "Aktywny"
    // W przeciwnym razie – "Ustaw jako aktywny" (klikalny).
    if (window.currentPreset.nowInUse) {
        activateBtn.textContent = "Aktywny";
        activateBtn.disabled = true;
    } else {
        activateBtn.textContent = "Ustaw jako aktywny";
        activateBtn.disabled = false;
        activateBtn.onclick = async function () {
            // Deaktywujemy customowe presety
            await deactivateAllPresets();
            // Ładujemy ponownie widok bazowy (teraz będzie active)
            await loadBaseView();
            // Odświeżamy dropdown
            await refreshPresetDropdown();
        };
    }

    // Ładujemy sklepy (All)
    loadCompetitors("All");
}


async function loadSelectedPreset(presetId) {
    if (!presetId) {
        const presetSelect = document.getElementById("presetSelect");
        if (presetSelect) presetId = presetSelect.value;
    }
    if (!presetId || presetId === "BASE") {
        loadBaseView();
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

        // Pokazujemy pole nazwy i przycisk edycji dla customowego presetu
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

        // Dynamiczny przycisk aktywacji dla customowego presetu
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

        // Dodajemy przycisk "Usuń preset" (tylko dla customowych presetów)
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
                        // Ustawiamy dropdown na widok bazowy
                        const presetSelect = document.getElementById("presetSelect");
                        presetSelect.value = "BASE";
                        loadBaseView();
                    } else {
                        alert("Błąd usuwania presetu: " + (data.message || ""));
                    }
                } catch (err) {
                    console.error("deletePreset error", err);
                }
            }
        };


        // Ustaw checkboxy źródeł – dla customowych widoków umożliwiamy zmianę
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
        // Dla customowego widoku umożliwiamy zmianę opcji "używaj nieoznaczonych sklepów"
        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
        useUnmarkedStoresCheckbox.disabled = false;

        const sourceVal = determineSourceVal(!!preset.sourceGoogle, !!preset.sourceCeneo);
        loadCompetitors(sourceVal);
    } catch (err) {
        console.error("loadSelectedPreset error", err);
    }
}


// Ustalanie filtra dla pobierania danych sklepów
function determineSourceVal(isGoogle, isCeneo) {
    if (isGoogle && isCeneo) return "All";
    if (isGoogle) return "Google";
    if (isCeneo) return "Ceneo";
    return "All";
}

// Ładuje dane sklepów i wypełnia tabele
function loadCompetitors(ourSource) {
    const url = `/PriceHistory/GetCompetitorStoresData?storeId=${storeId}&ourSource=${ourSource}`;
    fetch(url)
        .then(r => r.json())
        .then(data => {
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
        })
        .catch(err => console.error(err));
}

// Tworzy wiersz w tabeli z danymi o sklepie
function createRow(item) {
    const tr = document.createElement("tr");

    const tdStore = document.createElement("td");
    tdStore.textContent = item.storeName;
    tr.appendChild(tdStore);

    const tdSource = document.createElement("td");
    tdSource.textContent = item.dataSource;
    tr.appendChild(tdSource);

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
        addBtn.textContent = "Dodaj";
        addBtn.style.marginRight = "5px";
        addBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), true);
        });
        tdAction.appendChild(addBtn);

        const remBtn = document.createElement("button");
        remBtn.textContent = "Usuń";
        remBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), false);
        });
        tdAction.appendChild(remBtn);
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
        row.style.backgroundColor = item.useCompetitor ? "green" : "darkred";
    } else {
        row.style.backgroundColor = useUnmarked ? "lightgreen" : "lightcoral";
    }
}

// Dodanie nowego presetu
document.addEventListener("DOMContentLoaded", () => {
    const btnNew = document.getElementById("addNewPresetBtn");
    if (!btnNew) return;
    btnNew.addEventListener("click", async function () {
        const presetName = prompt("Podaj nazwę nowego presetu:", "");
        if (!presetName) return;
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
                alert(`Utworzono nowy preset ID=${data.presetId}`);
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

document.addEventListener("DOMContentLoaded", () => {
    const googleChk = document.getElementById("googleCheckbox");
    const ceneoChk = document.getElementById("ceneoCheckbox");
    if (googleChk && ceneoChk) {
        function onSourceChange() {
            // Dla widoku bazowego checkboxy są zablokowane – nic nie zmieniamy
            if (!window.currentPreset || window.currentPreset.presetId === null) {
                const val = determineSourceVal(googleChk.checked, ceneoChk.checked);
                loadCompetitors(val);
                return;
            }
            // Dla customowych presetów aktualizujemy właściwości
            window.currentPreset.sourceGoogle = googleChk.checked;
            window.currentPreset.sourceCeneo = ceneoChk.checked;
            // Zapisujemy zmiany, a następnie przeładowujemy dane sklepów wg nowego ustawienia źródła
            saveOrUpdatePreset().then(() => {
                const sourceVal = determineSourceVal(googleChk.checked, ceneoChk.checked);
                loadCompetitors(sourceVal);
            });
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

// Obsługa przycisków zamykania modala
var closeButtons = document.querySelectorAll('#competitorModal .close, #competitorModal [data-dismiss="modal"]');
closeButtons.forEach(function (btn) {
    btn.addEventListener('click', function () {
        var competitorModal = document.getElementById("competitorModal");
        competitorModal.style.display = 'none';
        competitorModal.classList.remove('show');
        loadPrices();
    });
});
