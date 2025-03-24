/*****************************************************
 * CompetitorModal.js – obsługa modala z presetami
 *****************************************************/

// OTWARCIE MODALA
window.openCompetitorsModal = async function () {
    const presets = await fetchPresets();

    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;

    presetSelect.innerHTML = "";
    // Bazowy
    const baseOpt = document.createElement("option");
    baseOpt.value = "BASE";
    baseOpt.textContent = "(Widok bazowy)";
    presetSelect.appendChild(baseOpt);

    // Reszta presetów
    presets.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p.presetId;
        opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
        presetSelect.appendChild(opt);
    });

    // Czy któryś jest NowInUse?
    const active = presets.find(x => x.nowInUse === true);
    if (active) {
        presetSelect.value = active.presetId;
        await loadSelectedPreset();
    } else {
        // Bazowy widok
        presetSelect.value = "BASE";
        loadBaseView();
    }

    $('#competitorModal').modal('show');
};

// POBRANIE LISTY PRESETÓW
async function fetchPresets() {
    const url = `/PriceHistory/GetPresets?storeId=${storeId}`;
    try {
        const resp = await fetch(url);
        return await resp.json();
    } catch (err) {
        console.error("Błąd fetchPresets:", err);
        return [];
    }
}

// WIDOK BAZOWY
function loadBaseView() {
    window.currentPreset = {
        presetId: null,
        presetName: "(Widok bazowy)",
        nowInUse: false,
        sourceGoogle: true,    // Google + Ceneo
        sourceCeneo: true,
        useUnmarkedStores: true,
        competitorItems: []
    };

    document.getElementById("newPresetSection").style.display = "none";
    loadCompetitors("All"); // param "All"
}

// PRZEŁĄCZANIE <SELECT> PRESET
document.addEventListener("DOMContentLoaded", function () {
    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;
    presetSelect.addEventListener("change", async function () {
        if (this.value === "BASE") {
            loadBaseView();
        } else {
            await loadSelectedPreset();
        }
    });
});

// WYCZYTANIE PRESETU Z SERWERA
async function loadSelectedPreset() {
    const presetSelect = document.getElementById("presetSelect");
    if (!presetSelect) return;
    const presetId = presetSelect.value;
    if (!presetId || presetId === "BASE") {
        loadBaseView();
        return;
    }
    try {
        const resp = await fetch(`/PriceHistory/GetPresetDetails?presetId=${presetId}`);
        const preset = await resp.json();
        if (!preset) {
            console.warn("Brak presetu w odpowiedzi");
            return;
        }
        window.currentPreset = preset;

        document.getElementById("newPresetSection").style.display = "block";
        document.getElementById("presetNameInput").value = preset.presetName || "";
        document.getElementById("nowInUseCheckbox").checked = preset.nowInUse;
        document.getElementById("useUnmarkedStoresCheckbox").checked = preset.useUnmarkedStores;

        // Ustaw sourceSelect
        let sourceVal = "All";
        if (preset.sourceGoogle && !preset.sourceCeneo) sourceVal = "Google";
        if (!preset.sourceGoogle && preset.sourceCeneo) sourceVal = "Ceneo";
        document.getElementById("sourceSelect").value = sourceVal;

        loadCompetitors(sourceVal);
    } catch (err) {
        console.error(err);
    }
}

// ŁADOWANIE KONKURENTÓW
function loadCompetitors(ourSource) {
    const url = `/PriceHistory/GetCompetitorStoresData?storeId=${storeId}&ourSource=${ourSource}`;
    fetch(url)
        .then(r => r.json())
        .then(data => {
            if (!data.data) {
                console.warn("Brak data w liście konkurentów");
                return;
            }
            const googleData = data.data.filter(x => x.dataSource === "Google");
            const ceneoData = data.data.filter(x => x.dataSource === "Ceneo");

            const gBody = document.getElementById("googleCompetitorsTableBody");
            const cBody = document.getElementById("ceneoCompetitorsTableBody");
            if (gBody) gBody.innerHTML = "";
            if (cBody) cBody.innerHTML = "";

            googleData.forEach(item => gBody.appendChild(createRow(item)));
            ceneoData.forEach(item => cBody.appendChild(createRow(item)));

            markPresetCompetitors();
        })
        .catch(err => console.error(err));
}

// TWORZENIE WIERSZA
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

    const tdAction = document.createElement("td");
    const addBtn = document.createElement("button");
    addBtn.textContent = "Dodaj";
    addBtn.style.marginRight = "5px";
    addBtn.addEventListener("click", () => {
        setCompetitorStatus(item.storeName, item.dataSource === "Google", true);
    });
    tdAction.appendChild(addBtn);

    const remBtn = document.createElement("button");
    remBtn.textContent = "Usuń";
    remBtn.addEventListener("click", () => {
        setCompetitorStatus(item.storeName, item.dataSource === "Google", false);
    });
    tdAction.appendChild(remBtn);

    tr.appendChild(tdAction);

    tr.dataset.storeName = item.storeName;
    tr.dataset.dataSource = item.dataSource; // "Google"/"Ceneo"

    return tr;
}

// DODAWANIE/USUWANIE SKLEPU
function setCompetitorStatus(storeName, isGoogle, useCompetitor) {
    if (!window.currentPreset || !window.currentPreset.presetId) {
        alert("Widok bazowy – nie zapisujemy do bazy.");
        return;
    }
    const payload = {
        presetId: window.currentPreset.presetId,
        storeName,
        isGoogle,
        useCompetitor
    };
    fetch("/PriceHistory/UpdateCompetitorItem", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    })
        .then(r => r.json())
        .then(data => {
            if (!data.success) {
                alert("Błąd zapisu: " + (data.message || ""));
                return;
            }
            if (!window.currentPreset.competitorItems) {
                window.currentPreset.competitorItems = [];
            }
            let item = window.currentPreset.competitorItems.find(ci => ci.storeName === storeName && ci.isGoogle === isGoogle);
            if (!item) {
                item = { storeName, isGoogle, useCompetitor };
                window.currentPreset.competitorItems.push(item);
            } else {
                item.useCompetitor = useCompetitor;
            }
            refreshRowColor(storeName, isGoogle);
        })
        .catch(err => console.error(err));
}

// KOLORY
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
    if (!row) return;

    let item = null;
    if (window.currentPreset && window.currentPreset.competitorItems) {
        item = window.currentPreset.competitorItems.find(ci => ci.storeName === storeName && ci.isGoogle === isGoogle);
    }
    const useUnmarked = window.currentPreset ? window.currentPreset.useUnmarkedStores : true;

    if (item) {
        row.style.backgroundColor = item.useCompetitor ? "green" : "darkred";  // ciemnozielony/czerwony
    } else {
        row.style.backgroundColor = useUnmarked ? "lightgreen" : "lightcoral"; // jasnozielony/czerwony
    }
}

// useUnmarkedStores
document.addEventListener("DOMContentLoaded", function () {
    const chk = document.getElementById("useUnmarkedStoresCheckbox");
    if (!chk) return;
    chk.addEventListener("change", async function () {
        if (!window.currentPreset) return;
        if (!window.currentPreset.presetId) {
            window.currentPreset.useUnmarkedStores = this.checked;
            markPresetCompetitors();
            return;
        }
        const payload = {
            presetId: window.currentPreset.presetId,
            useUnmarkedStores: this.checked
        };
        try {
            const resp = await fetch("/PriceHistory/SetUseUnmarkedStores", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            const data = await resp.json();
            if (!data.success) {
                alert("Błąd zapisu useUnmarkedStores");
                return;
            }
            window.currentPreset.useUnmarkedStores = this.checked;
            markPresetCompetitors();
        } catch (err) {
            console.error(err);
        }
    });
});

// DODAWANIE NOWEGO PRESETU
document.addEventListener("DOMContentLoaded", function () {
    const btnNew = document.getElementById("addNewPresetBtn");
    if (!btnNew) return;
    btnNew.addEventListener("click", async function () {
        const presetName = prompt("Podaj nazwę nowego presetu:", "");
        if (!presetName) return;

        const payload = {
            storeId: storeId,
            presetName,
            nowInUse: false,
            sourceGoogle: true,
            sourceCeneo: true,
            useUnmarkedStores: true,
            competitorItems: []
        };
        try {
            const resp = await fetch("/PriceHistory/SaveCompetitorPreset", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            const data = await resp.json();
            if (data.success) {
                alert(`Utworzono nowy preset ID=${data.presetId}`);
                const newPresets = await fetchPresets();
                const presetSelect = document.getElementById("presetSelect");
                if (!presetSelect) return;

                presetSelect.innerHTML = "";
                const baseOpt = document.createElement("option");
                baseOpt.value = "BASE";
                baseOpt.textContent = "(Widok bazowy)";
                presetSelect.appendChild(baseOpt);

                newPresets.forEach(p => {
                    const opt = document.createElement("option");
                    opt.value = p.presetId;
                    opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
                    presetSelect.appendChild(opt);
                });
                presetSelect.value = data.presetId;
                await loadSelectedPreset();
            } else {
                alert("Błąd tworzenia nowego presetu");
            }
        } catch (err) {
            console.error(err);
        }
    });
});

// ZMIANA NAZWY I NOWINUSE
document.addEventListener("DOMContentLoaded", function () {
    const editBtn = document.getElementById("editPresetBtn");
    if (!editBtn) return;

    editBtn.addEventListener("click", async function () {
        if (!window.currentPreset || !window.currentPreset.presetId) {
            alert("Widok bazowy lub brak wybranego presetu");
            return;
        }
        const nameInput = document.getElementById("presetNameInput");
        const nowInUseCheckbox = document.getElementById("nowInUseCheckbox");
        if (!nameInput || !nowInUseCheckbox) return;

        const newName = nameInput.value.trim();
        const newNowInUse = nowInUseCheckbox.checked;

        // Twój endpoint do zmiany nazwy i NowInUse
        // np. /PriceHistory/UpdatePresetName
        const payload = {
            presetId: window.currentPreset.presetId,
            presetName: newName,
            nowInUse: newNowInUse,
            // ewentualnie sourceGoogle, sourceCeneo – jeśli chcesz je też tam zmieniać
        };
        try {
            // ZMIENIŁEŚ NAZWA. UWAGA: Twój endpoint musi istnieć, bo tu robimy fetch:
            const resp = await fetch("/PriceHistory/UpdatePresetName", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (!resp.ok) {
                // np. 404 => brak akcji
                throw new Error("Status " + resp.status);
            }
            const data = await resp.json();
            if (!data.success) {
                alert("Błąd zapisu nazwy/NowInUse");
                return;
            }
            alert("Zmieniono nazwę presetu!");

            // Odśwież local
            window.currentPreset.presetName = newName;
            window.currentPreset.nowInUse = newNowInUse;

            // Odśwież listę w <select>
            const updatedPresets = await fetchPresets();
            const presetSelect = document.getElementById("presetSelect");
            if (!presetSelect) return;
            presetSelect.innerHTML = "";

            const baseOpt = document.createElement("option");
            baseOpt.value = "BASE";
            baseOpt.textContent = "(Widok bazowy)";
            presetSelect.appendChild(baseOpt);

            updatedPresets.forEach(p => {
                const opt = document.createElement("option");
                opt.value = p.presetId;
                opt.textContent = p.presetName + (p.nowInUse ? " [aktywny]" : "");
                presetSelect.appendChild(opt);
            });

            // Ustaw zaktualizowany preset jako wybrany
            presetSelect.value = window.currentPreset.presetId;
        } catch (err) {
            console.error("Błąd zapisu nazwy", err);
            alert("Błąd zapisu nazwy lub endpoint /UpdatePresetName nie istnieje!");
        }
    });
});
