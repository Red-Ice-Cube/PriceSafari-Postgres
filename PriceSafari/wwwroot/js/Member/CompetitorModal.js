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

   
    const anyActive = newPresets.some(p => p.nowInUse === true);

  
    const baseOpt = document.createElement("option");
    baseOpt.value = "BASE";
    if (!anyActive) {
        baseOpt.textContent = "PriceSafari - Wszystkie Dane [aktywny]";
    } else {
        baseOpt.textContent = "PriceSafari - Wszystkie Dane";
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

     
        window.currentPreset = {
            presetId: null,
            storeId: storeId,
            presetName: "PriceSafari - Wszystkie Dane",
            nowInUse: !customActive, 
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

     
        const googleCheckbox = document.getElementById("googleCheckbox");
        googleCheckbox.checked = window.currentPreset.sourceGoogle;
        googleCheckbox.disabled = true;
        const ceneoCheckbox = document.getElementById("ceneoCheckbox");
        ceneoCheckbox.checked = window.currentPreset.sourceCeneo;
        ceneoCheckbox.disabled = true;
        const useUnmarkedStoresCheckbox = document.getElementById("useUnmarkedStoresCheckbox");
        useUnmarkedStoresCheckbox.checked = window.currentPreset.useUnmarkedStores;
        useUnmarkedStoresCheckbox.disabled = true;


        // Przygotowujemy / pobieramy button aktywacji
        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            activateBtn = document.createElement("button");
            activateBtn.id = "activatePresetBtn";
            document.getElementById("activateButtonContainer").appendChild(activateBtn);
        }

        // Nadajemy podstawową klasę:
        activateBtn.className = "Button-Page-Small";

        // Jeśli preset jest aktywny:
        if (window.currentPreset.nowInUse) {
            activateBtn.textContent = "Aktywny";
            activateBtn.disabled = true;

            // Dodajemy klasę z zielonym tłem:
            activateBtn.classList.add("active-preset");
        } else {
            // Jeśli nie jest aktywny, usuwamy klasę zielonego tła:
            activateBtn.classList.remove("active-preset");

            activateBtn.textContent = "Ustaw jako aktywny";
            activateBtn.disabled = false;

            // Onclick – dezaktywujemy wszystkie i ponownie ładujemy bazę
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
        // Przygotowujemy / pobieramy button aktywacji
        let activateBtn = document.getElementById("activatePresetBtn");
        if (!activateBtn) {
            activateBtn = document.createElement("button");
            activateBtn.id = "activatePresetBtn";
            document.getElementById("activateButtonContainer").appendChild(activateBtn);
        }

        // Nadajemy tę samą podstawową klasę:
        activateBtn.className = "Button-Page-Small";

        if (window.currentPreset.nowInUse) {
            activateBtn.textContent = "Aktywny";
            activateBtn.disabled = true;
            // Dodajemy klasę z zielonym tłem
            activateBtn.classList.add("active-preset");
        } else {
            activateBtn.textContent = "Ustaw jako aktywny";
            activateBtn.disabled = false;
            // Usuwamy, gdyby wcześniej była
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
            // Ustawiamy zawartość, style i tooltip dla istniejącego przycisku
            editBtn.innerHTML = '<i class="fas fa-pen" style="color:#4e4e4e; font-size:16px;"></i>';
            editBtn.title = "Zmień nazwę presetu";
            editBtn.style.border = "none";
            editBtn.style.borderRadius = "4px";
            editBtn.style.width = "33px";
            editBtn.style.height = "33px";
            editBtn.style.background = "#e3e3e3";

            editBtn.style.cursor = "pointer";
        } else {
            // Tworzymy nowy przycisk z ustawieniami
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
            // Wyświetlamy prompt z aktualną nazwą jako domyślną wartością
            const newName = prompt("Podaj nową nazwę presetu:", window.currentPreset.presetName);
            if (newName && newName.trim() !== "") {
                window.currentPreset.presetName = newName.trim();
                await saveOrUpdatePreset();
                await refreshPresetDropdown();
                alert("Zmieniono nazwę presetu.");
            }
        };




        // Konfiguracja przycisku "Usuń preset" – umieszczamy go w deleteButtonContainer
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
        // Przycisk "Dodaj" z ikoną tick
        const addBtn = document.createElement("button");
        addBtn.classList.add("filterCompetitors-true"); // dodajemy klasę CSS
        addBtn.style.marginRight = "5px";
        addBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), true);
        });
        addBtn.innerHTML = '<i class="fas fa-check"></i>';
        tdAction.appendChild(addBtn);

        // Przycisk "Usuń" z ikoną X
        const remBtn = document.createElement("button");
        remBtn.classList.add("filterCompetitors-false");
        remBtn.style.marginRight = "5px";
        remBtn.addEventListener("click", () => {
            toggleCompetitorUsage(item.storeName, (item.dataSource === "Google"), false);
        });
        remBtn.innerHTML = '<i class="fas fa-times"></i>';
        tdAction.appendChild(remBtn);

        // Przycisk "Oczyść" z ikoną strzałki cofania
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

// Aktualizacja nazwy presetu (customowych widoków) – używamy prompt
document.addEventListener("DOMContentLoaded", () => {
    const editBtn = document.getElementById("editPresetBtn");
    if (editBtn) {
        editBtn.addEventListener("click", async function () {
            if (!window.currentPreset || !window.currentPreset.presetId) {
                alert("Nie można zmienić nazwy presetu.");
                return;
            }
            // Wyświetlamy prompt z aktualną nazwą jako wartość domyślną
            let presetName = prompt("Podaj nazwę nowego presetu (max 40 znaków):", window.currentPreset.presetName);
            if (!presetName) return;
            presetName = presetName.trim();
            if (presetName.length > 40) {
                presetName = presetName.substring(0, 40);
                alert("Nazwa została przycięta do 40 znaków.");
            }
            window.currentPreset.presetName = presetName;
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


function filterCompetitors(searchTerm) {
    const allRows = document.querySelectorAll("#googleCompetitorsTableBody tr, #ceneoCompetitorsTableBody tr");

    allRows.forEach(tr => {
        const originalStoreName = tr.dataset.originalStoreName || "";
        const storeNameLower = originalStoreName.toLowerCase();
        const searchLower = searchTerm.toLowerCase();

        const storeTd = tr.querySelector("td:nth-child(1)");
        if (!storeTd) return; 

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
    if (!searchTerm) return fullText; 

    const escapedTerm = searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');


    const regex = new RegExp(`(${escapedTerm})`, 'gi');

    return fullText.replace(regex, '<span style="font-weight:bold; color:yellow;">$1</span>');
}

document.addEventListener('click', function (event) {
    const modal = document.getElementById('competitorModal');
    if (modal.classList.contains('show')) {
        const dialog = modal.querySelector('.modal-simulation');
        if (!dialog.contains(event.target)) {
      
            modal.style.display = 'none';
            modal.classList.remove('show');
            loadPrices(); 
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
  
    const competitorModal = document.getElementById("competitorModal");
    competitorModal.addEventListener("click", function (event) {
        const closeBtn = event.target.closest("[data-dismiss='modal']");
        if (closeBtn) {
            competitorModal.style.display = "none";
            competitorModal.classList.remove("show");
            loadPrices();
        }
    });

   
    const competitorSearchInput = document.getElementById("competitorSearchInput");
    if (competitorSearchInput) {
        competitorSearchInput.addEventListener("input", function () {
            filterCompetitors(this.value);
        });
    }
});
