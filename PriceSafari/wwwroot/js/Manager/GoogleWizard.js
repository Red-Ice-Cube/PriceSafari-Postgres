const googleDataEl = document.getElementById("google-wizard-data");

// Odczytujemy storeId (z parsowaniem do liczby)
const storeId = parseInt(googleDataEl.getAttribute("data-store-id") || "0", 10);

// Odczytujemy JSON mapowań
let existingMappingsJson = googleDataEl.getAttribute("data-existing-mappings") || "[]";

// Parsujemy JSON
let existingMappings = [];
try {
    existingMappings = JSON.parse(existingMappingsJson);
} catch (e) {
    console.error("Błąd parsowania existingMappingsJson:", e);
    existingMappings = [];
}

// Pola, w tym nowe ceny
let mappingForField = {
    "ExternalId": null,
    "Url": null,
    "GoogleEan": null,
    "GoogleImage": null,
    "GoogleExportedName": null,
    "GoogleXMLPrice": null,
    "GoogleDeliveryXMLPrice": null
};

existingMappings.forEach(m => {
    if (m.fieldName) {
        mappingForField[m.fieldName] = {
            xpath: m.localName,
            nodeCount: 0,
            firstValue: ""
        };
    }
});

let xmlDoc = null;
let proxyUrl = `/GoogleImportWizardXml/ProxyXml?storeId=${storeId}`;

// ==================================
// 1. Pobieranie XML przez fetch
// ==================================
fetch(proxyUrl)
    .then(resp => resp.text())
    .then(xmlStr => {
        let parser = new DOMParser();
        xmlDoc = parser.parseFromString(xmlStr, "application/xml");
        if (xmlDoc.documentElement.nodeName === "parsererror") {
            document.getElementById("xmlContainer").innerText =
                "Błąd parsowania XML:\n" + xmlDoc.documentElement.textContent;
            return;
        }
        // Budujemy drzewo w stylu Ceneo (rekurencyjnie)
        buildXmlTree(xmlDoc.documentElement, "");
        // Nakładamy istniejące mapowania z bazy
        applyExistingMappings();
    })
    .catch(err => {
        document.getElementById("xmlContainer").innerText =
            "Błąd pobierania XML:\n" + err;
    });

// ==================================
// 2. Funkcje do budowy DRZEWA XML (na wzór Ceneo)
// ==================================
function buildXmlTree(node, parentPath) {
    let container = document.getElementById("xmlContainer");
    if (!parentPath && !container.querySelector("ul")) {
        let ulRoot = document.createElement("ul");
        ulRoot.appendChild(createLi(node, parentPath));
        container.appendChild(ulRoot);
    } else {
        let rootUl = container.querySelector("ul");
        rootUl.appendChild(createLi(node, parentPath));
    }
}

function createLi(node, parentPath) {
    let li = document.createElement("li");
    li.classList.add("xml-node");

    // Nazwa + ewentualny atrybut name
    let nodeName = node.nodeName;
    let nameAttr = node.getAttribute && node.getAttribute("name");
    if (nameAttr) {
        nodeName += `[@name="${nameAttr}"]`;
    }

    // Budujemy pełny XPath
    let currentPath = parentPath
        ? parentPath + "/" + nodeName
        : "/" + nodeName;

    li.setAttribute("data-xpath", currentPath);

    // Nagłówek węzła
    let b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    // Atrybuty (oprócz name, bo wyżej już obsłużyliśmy)
    if (node.attributes && node.attributes.length > 0) {
        let ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {
            // Pomijamy atrybut "name", bo poszedł wyżej
            if (attr.name === "name") return;

            let attrLi = document.createElement("li");
            attrLi.classList.add("xml-node");
            let attrPath = currentPath + `/@${attr.name}`;
            attrLi.setAttribute("data-xpath", attrPath);

            let attrSpan = document.createElement("span");
            attrSpan.innerText = `${attr.value}`;
            attrLi.appendChild(attrSpan);

            ulAttrs.appendChild(attrLi);
        });
        if (ulAttrs.children.length > 0) {
            li.appendChild(ulAttrs);
        }
    }

    // Jeśli brak dzieci, ale jest tekst => #value
    let textVal = node.textContent.trim();
    if (node.children.length === 0 && textVal) {
        let ulVal = document.createElement("ul");
        let liVal = document.createElement("li");
        liVal.classList.add("xml-node");
        let valPath = currentPath + "/#value";
        liVal.setAttribute("data-xpath", valPath);

        let spanVal = document.createElement("span");
        spanVal.innerText = textVal;
        liVal.appendChild(spanVal);

        ulVal.appendChild(liVal);
        li.appendChild(ulVal);
    }

    // Rekurencja: dzieci
    if (node.children.length > 0) {
        let ul = document.createElement("ul");
        Array.from(node.children).forEach(child => {
            ul.appendChild(createLi(child, currentPath));
        });
        li.appendChild(ul);
    }

    return li;
}

// ==================================
// 3. Klik w węzeł = przypisanie Xpath
// ==================================
document.addEventListener("click", function (e) {
    let el = e.target.closest(".xml-node");
    if (!el) return;
    let xPath = el.getAttribute("data-xpath");
    let selectedField = document.getElementById("fieldSelector").value;

    // Czyścimy stare highlighty dla tego pola
    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    // Zaznaczamy nowe
    let sameNodes = document.querySelectorAll(`.xml-node[data-xpath='${xPath}']`);
    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    // Wyliczamy ile węzłów oraz pierwszy tekst
    let count = sameNodes.length;
    let firstVal = "";
    if (count > 0) {
        let sp = sameNodes[0].querySelector("span");
        if (sp) {
            firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
        }
    }

    // Zapis do naszego obiektu
    mappingForField[selectedField] = {
        xpath: xPath,
        nodeCount: count,
        firstValue: firstVal
    };
    renderMappingTable();
});

// ==================================
// 4. Przywracanie mapowań z bazy
// ==================================
function applyExistingMappings() {
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (!info || !info.xpath) continue;

        let sameNodes = document.querySelectorAll(`.xml-node[data-xpath='${info.xpath}']`);
        let count = sameNodes.length;
        let firstVal = "";
        if (count > 0) {
            let sp = sameNodes[0].querySelector("span");
            if (sp) {
                firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
            }
        }
        info.nodeCount = count;
        info.firstValue = firstVal;
        sameNodes.forEach(n => n.classList.add(`highlight-${fieldName}`));
    }
    renderMappingTable();
}

function clearAllHighlights() {
    Object.keys(mappingForField).forEach(field => {
        document.querySelectorAll(`.highlight-${field}`)
            .forEach(el => el.classList.remove(`highlight-${field}`));
    });
}

// ==================================
// 5. Render mapowań w tabelce
// ==================================
function renderMappingTable() {
    let tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = "";
    for (let fieldName in mappingForField) {
        if (!fieldName || fieldName === "undefined") continue;
        let info = mappingForField[fieldName];
        let tr = document.createElement("tr");
        if (!info) {
            tr.innerHTML = `
                  <td>${fieldName}</td>
                  <td>-</td>
                  <td>0</td>
                  <td>-</td>
                `;
        } else {
            tr.innerHTML = `
                  <td>${fieldName}</td>
                  <td>${info.xpath || "-"}</td>
                  <td>${info.nodeCount || 0}</td>
                  <td>${info.firstValue || "-"}</td>
                `;
        }
        tbody.appendChild(tr);
    }
}

// ==================================
// 6. Zapis mapowań w bazie
// ==================================
document.getElementById("saveMapping").addEventListener("click", function () {
    let finalMappings = [];
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (info && info.xpath) {
            finalMappings.push({
                fieldName: fieldName,
                localName: info.xpath
            });
        }
    }
    fetch(`/GoogleImportWizardXml/SaveGoogleMappings?storeId=${storeId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(finalMappings)
    })
        .then(r => r.json())
        .then(d => alert(d.message))
        .catch(err => console.error(err));
});

// ==================================
// 7. Ponowne wczytanie mapowań
// ==================================
document.getElementById("reloadMappings").addEventListener("click", function () {
    fetch(`/GoogleImportWizardXml/GetGoogleMappings?storeId=${storeId}`)
        .then(r => r.json())
        .then(data => {
            console.log("Odebrano mapowania z bazy (reload):", data);
            clearAllHighlights();

            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);
            data.forEach(m => {
                if (m.fieldName) {
                    mappingForField[m.fieldName] = {
                        xpath: m.localName,
                        nodeCount: 0,
                        firstValue: ""
                    };
                }
            });
            applyExistingMappings();
        })
        .catch(err => console.error("Błąd getGoogleMappings:", err));
});

// ==================================
// 8. Funkcja parsePrice - wyłuskuje liczbę z tekstu "Koszt 123,45 PLN" -> "123.45"
// ==================================
function parsePrice(value) {
    if (!value) return null;
    let match = value.match(/([\d]+([.,]\d+)?)/);
    if (!match) return null;
    let numericString = match[1].replace(',', '.');
    let floatVal = parseFloat(numericString);
    if (isNaN(floatVal)) return null;
    return floatVal.toFixed(2); // "xx.xx"
}

// ==================================
// 9. Wyciąganie produktów z XML
// ==================================
function extractProductsFromXml() {
    if (!xmlDoc) {
        alert("Brak XML do parsowania");
        return;
    }
    let entries = xmlDoc.getElementsByTagName("item").length > 0
        ? xmlDoc.getElementsByTagName("item")
        : xmlDoc.getElementsByTagName("entry");
    let productMaps = [];
    let countUrlsWithParams = 0;
    let removeParams = document.getElementById("cleanUrlParameters").checked;

    for (let i = 0; i < entries.length; i++) {
        let entryNode = entries[i];
        let pm = {
            StoreId: storeId.toString(),
            ExternalId: getVal(entryNode, "ExternalId"),
            Url: getVal(entryNode, "Url"),
            GoogleEan: getVal(entryNode, "GoogleEan"),
            GoogleImage: getVal(entryNode, "GoogleImage"),
            GoogleExportedName: getVal(entryNode, "GoogleExportedName"),
            GoogleXMLPrice: parsePrice(getVal(entryNode, "GoogleXMLPrice")),
            GoogleDeliveryXMLPrice: parsePrice(getVal(entryNode, "GoogleDeliveryXMLPrice"))
        };

        // Usuwanie parametrów z URL
        if (pm.Url) {
            let qIdx = pm.Url.indexOf('?');
            if (qIdx !== -1) {
                countUrlsWithParams++;
                if (removeParams) {
                    pm.Url = pm.Url.substring(0, qIdx);
                }
            }
        }

        // Przykład: jeśli EAN jest pusty, pomijamy
        if (!pm.GoogleEan || pm.GoogleEan.trim() === "") {
            continue;
        }

        productMaps.push(pm);
    }

    console.log("Wyciągnięto productMaps(front):", productMaps);
    console.log("Liczba URLi zawierających parametry:", countUrlsWithParams);

    // Ustawiamy JSON w polu podglądu
    let previewText = JSON.stringify(productMaps, null, 2);
    document.getElementById("productMapsPreview").textContent = previewText;

    // Informacja o parametrach URL
    document.getElementById("urlParamsInfo").textContent =
        "Liczba URL zawierających parametry: " + countUrlsWithParams;

    // Ewentualna deduplikacja URL
    if (document.getElementById("removeDuplicateUrls").checked) {
        let seen = {};
        let deduped = [];
        productMaps.forEach(pm => {
            if (pm.Url) {
                if (!seen[pm.Url]) {
                    seen[pm.Url] = true;
                    deduped.push(pm);
                }
            } else {
                deduped.push(pm);
            }
        });
        productMaps = deduped;
        document.getElementById("productMapsPreview").textContent =
            JSON.stringify(productMaps, null, 2);
    }

    // Ewentualna deduplikacja EAN
    if (document.getElementById("removeDuplicateEans").checked) {
        let seenEan = {};
        let dedupedEan = [];
        productMaps.forEach(pm => {
            if (pm.GoogleEan) {
                if (!seenEan[pm.GoogleEan]) {
                    seenEan[pm.GoogleEan] = true;
                    dedupedEan.push(pm);
                }
            } else {
                dedupedEan.push(pm);
            }
        });
        productMaps = dedupedEan;
        document.getElementById("productMapsPreview").textContent =
            JSON.stringify(productMaps, null, 2);
    }

    // Finalna liczba węzłów
    document.getElementById("finalNodesInfo").textContent =
        "Final nodes po filtrach: " + productMaps.length;

    // Sprawdzamy duplikaty
    checkDuplicates(productMaps);
}

// ==================================
// 10. Funkcja sprawdzająca duplikaty
// ==================================
function checkDuplicates(productMaps) {
    let urlCounts = {};
    let eanCounts = {};

    productMaps.forEach(pm => {
        if (pm.Url) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        }
        if (pm.GoogleEan) {
            eanCounts[pm.GoogleEan] = (eanCounts[pm.GoogleEan] || 0) + 1;
        }
    });

    let totalUniqueUrls = Object.keys(urlCounts).length;
    let duplicateUrls = 0;
    for (let key in urlCounts) {
        if (urlCounts[key] > 1) {
            duplicateUrls++;
        }
    }

    let totalUniqueEans = Object.keys(eanCounts).length;
    let duplicateEans = 0;
    for (let key in eanCounts) {
        if (eanCounts[key] > 1) {
            duplicateEans++;
        }
    }

    let urlMessage = `Unikalnych URL: ${totalUniqueUrls} `;
    urlMessage += duplicateUrls > 0
        ? `<span style="color:red;">(Duplikatów: ${duplicateUrls})</span>`
        : `(Brak duplikatów)`;

    let eanMessage = `Unikalnych kodów EAN: ${totalUniqueEans} `;
    eanMessage += duplicateEans > 0
        ? `<span style="color:red;">(Duplikatów: ${duplicateEans})</span>`
        : `(Brak duplikatów)`;

    document.getElementById("duplicatesInfo").innerHTML =
        urlMessage + "<br>" + eanMessage;
}

// ==================================
// 11. Obsługa przycisków 'extractProducts' i checkboksów
// ==================================
document.getElementById("extractProducts").addEventListener("click", function () {
    extractProductsFromXml();
});

document.getElementById("cleanUrlParameters").addEventListener("change", function () {
    extractProductsFromXml();
});

document.getElementById("removeDuplicateUrls").addEventListener("change", function () {
    extractProductsFromXml();
});

document.getElementById("removeDuplicateEans").addEventListener("change", function () {
    extractProductsFromXml();
});

// ==================================
// 12. Funkcja getVal -> obsługuje multi-level path
// ==================================
function getVal(entryNode, fieldName) {
    let info = mappingForField[fieldName];
    if (!info || !info.xpath) return null;

    let path = info.xpath;

    // Usuwamy najczęstsze prefiksy (np. "/rss/channel/item/")
    let possiblePrefixes = ["/rss/channel/item/", "/feed/entry/"];
    for (let i = 0; i < possiblePrefixes.length; i++) {
        if (path.startsWith(possiblePrefixes[i])) {
            path = path.substring(possiblePrefixes[i].length);
            break;
        }
    }
    if (path.startsWith("/")) {
        path = path.substring(1);
    }

    // Rozbijamy po slashach, żeby obsłużyć np. "g:shipping/g:price"
    let segments = path.split('/');

    let currentNode = entryNode;
    for (let seg of segments) {
        // Obsługujemy np. "g:shipping" -> "shipping"
        if (seg.indexOf(':') !== -1) {
            seg = seg.split(':')[1];
        }
        if (!seg) return null;

        let child = Array.from(currentNode.children).find(e => e.localName === seg);
        if (!child) {
            return null;
        }
        currentNode = child;
    }
    return currentNode.textContent.trim();
}


document.getElementById("saveProductMapsInDb").addEventListener("click", function () {
    let txt = document.getElementById("productMapsPreview").textContent.trim();
    if (!txt) {
        alert("Brak productMaps do zapisania!");
        return;
    }
    let productMaps = [];
    try {
        productMaps = JSON.parse(txt);
    } catch (e) {
        alert("Błędny JSON w productMapsPreview!");
        return;
    }
    fetch("/GoogleImportWizardXml/SaveProductMapsFromFront", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productMaps)
    })
        .then(r => r.json())
        .then(d => alert("Zapisano: " + d.message))
        .catch(err => console.error(err));
});

// Pierwsze wyrenderowanie tabeli mapowań
renderMappingTable();