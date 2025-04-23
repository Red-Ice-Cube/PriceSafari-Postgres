const ceneoDataEl = document.getElementById("ceneo-wizard-data");
const storeId = parseInt(ceneoDataEl.getAttribute("data-store-id") || "0", 10);
let existingMappingsJson = ceneoDataEl.getAttribute("data-existing-mappings") || "[]";
let existingMappings = [];
try {

    existingMappings = JSON.parse(existingMappingsJson);
} catch (e) {
    console.error("Błąd parsowania existingMappingsJson:", e);
    existingMappings = [];
}

let mappingForField = {
    ExternalId: null,
    Url: null,
    CeneoEan: null,
    CeneoImage: null,
    CeneoExportedName: null,
    CeneoXMLPrice: null,
    CeneoDeliveryXMLPrice: null
};

existingMappings.forEach(m => {

    if (m.fieldName && mappingForField.hasOwnProperty(m.fieldName)) {
        mappingForField[m.fieldName] = {
            xpath: m.localName,
            nodeCount: 0,
            firstValue: ""
        };
    }
});

let xmlDoc = null;

let proxyUrl = `/CeneoImportWizardXml/ProxyXml?storeId=${storeId}`;

fetch(proxyUrl)
    .then(resp => {
        if (!resp.ok) {

            throw new Error(`Błąd HTTP ${resp.status}: ${resp.statusText}`);
        }
        return resp.text();
    })
    .then(xmlStr => {
        let parser = new DOMParser();
        xmlDoc = parser.parseFromString(xmlStr, "application/xml");

        if (xmlDoc.documentElement.nodeName === "parsererror" || xmlDoc.getElementsByTagName("parsererror").length > 0) {
            let errorMsg = "Błąd parsowania XML:\n";

            const parserError = xmlDoc.getElementsByTagName("parsererror")[0];
            if (parserError) {
                errorMsg += parserError.textContent;
            } else {
                errorMsg += xmlDoc.documentElement.textContent;
            }
            document.getElementById("xmlContainer").innerText = errorMsg;
            xmlDoc = null;
            return;
        }

        buildXmlTree(xmlDoc.documentElement, "");

        applyExistingMappings();
    })
    .catch(err => {

        document.getElementById("xmlContainer").innerText =
            "Błąd pobierania lub parsowania XML:\n" + err;
        console.error("Błąd fetch/parse XML:", err);
    });

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

    let nodeName = node.nodeName;

    let nameAttr = node.getAttribute && node.getAttribute("name");
    if (nameAttr) {

        nodeName += `[@name="${nameAttr}"]`;
    }

    let currentPath = parentPath ? parentPath + "/" + nodeName : "/" + nodeName;
    li.setAttribute("data-xpath", currentPath);

    let b = document.createElement("b");
    b.innerText = node.nodeName + (nameAttr ? ` (name="${nameAttr}")` : "");
    li.appendChild(b);

    if (node.attributes && node.attributes.length > 0) {
        let ulAttrs = document.createElement("ul");
        Array.from(node.attributes).forEach(attr => {

            if (attr.name === "name" && nameAttr) return;

            let attrLi = document.createElement("li");
            attrLi.classList.add("xml-node", "xml-attribute");

            let attrPath = currentPath + `/@${attr.name}`;
            attrLi.setAttribute("data-xpath", attrPath);

            let attrLabel = document.createElement("span");

            attrLabel.innerHTML = `<i style="color: gray;">@${attr.name}:</i> ${attr.value}`;
            attrLi.appendChild(attrLabel);
            ulAttrs.appendChild(attrLi);
        });
        if (ulAttrs.children.length > 0) {
            li.appendChild(ulAttrs);
        }
    }

    let textVal = node.textContent.trim();

    if (node.children.length === 0 && textVal) {
        let ulVal = document.createElement("ul");
        let liVal = document.createElement("li");
        liVal.classList.add("xml-node", "xml-value");

        let valPath = currentPath + "/#value";
        liVal.setAttribute("data-xpath", valPath);

        let spanVal = document.createElement("span");
        spanVal.innerText = `: ${textVal}`;
        liVal.appendChild(spanVal);
        ulVal.appendChild(liVal);
        li.appendChild(ulVal);
    }

    if (node.children.length > 0) {
        let ul = document.createElement("ul");
        Array.from(node.children).forEach(child => {

            ul.appendChild(createLi(child, currentPath));
        });
        li.appendChild(ul);
    }
    return li;
}

document.addEventListener("click", e => {

    let el = e.target.closest(".xml-node");
    if (!el) return;

    let selectedField = document.getElementById("fieldSelector").value;

    let xPath = el.getAttribute("data-xpath");

    document.querySelectorAll(`.highlight-${selectedField}`)
        .forEach(x => x.classList.remove(`highlight-${selectedField}`));

    let sameNodes = queryNodesByXPath(xPath);

    sameNodes.forEach(n => n.classList.add(`highlight-${selectedField}`));

    let count = sameNodes.length;
    let firstVal = "";
    if (count > 0) {

        let sp = sameNodes[0].querySelector("span");
        if (sp) {

            firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();

            if (sameNodes[0].classList.contains('xml-attribute')) {
                firstVal = sp.textContent.replace(/^(\s*@\w+:\s*)/, "").trim();
            }
        }
    }

    mappingForField[selectedField] = { xpath: xPath, nodeCount: count, firstValue: firstVal };

    renderMappingTable();
});

function queryNodesByXPath(xPathValue) {

    let esc = escapeForSelector(xPathValue);
    return document.querySelectorAll(`.xml-node[data-xpath="${esc}"]`);
}

function escapeForSelector(val) {
    if (!val) return "";

    return val.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}

function applyExistingMappings() {
    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];

        if (!info || !info.xpath) continue;

        let sameNodes = queryNodesByXPath(info.xpath);
        let count = sameNodes.length;
        let firstVal = "";
        if (count > 0) {

            let sp = sameNodes[0].querySelector("span");
            if (sp) {
                firstVal = sp.innerText.replace(/^(\s*:\s*)/, "").trim();
                if (sameNodes[0].classList.contains('xml-attribute')) {
                    firstVal = sp.textContent.replace(/^(\s*@\w+:\s*)/, "").trim();
                }
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

function renderMappingTable() {
    let tbody = document.getElementById("mappingTable").querySelector("tbody");
    tbody.innerHTML = "";

    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        let tr = document.createElement("tr");

        if (!info || !info.xpath) {
            tr.innerHTML = `<td>${fieldName}</td><td>-</td><td>0</td><td>-</td>`;
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

function parsePrice(value) {
    // 1. Sprawdzenie podstawowe
    if (value === null || value === undefined || value === '') return null;

    // 2. Konwersja na string i czyszczenie podstawowe
    let strValue = String(value).trim();

    // 3. Ekstrakcja części numerycznej (bardziej odporna na np. symbole walut)
    // Próbuje znaleźć ciąg cyfr, dopuszczając jeden separator (kropkę lub przecinek) dziesiętny.
    // Ignoruje początkowe i końcowe znaki nienumeryczne (np. "PLN 1.234,56 zł" -> "1.234,56")
    let match = strValue.match(/^[^\d-]*?([+-]?\s?[\d.,\s]+(?:\.\d+)?(?:,\d+)?)[^\d.,]*?$/);

    // Fallback do prostszego regexu, jeśli pierwszy zawiedzie
    if (!match) {
        match = strValue.match(/^[+-]?([\d.,\s]+)/);
    }

    // Jeśli nadal brak dopasowania, zwróć null
    if (!match || !match[1]) {
        console.warn(`parsePrice: Nie udało się wyekstrahować części numerycznej z '${value}'`);
        return null;
    }

    let numericString = match[1].trim(); // Wyekstrahowana część numeryczna

    // 4. Identyfikacja separatorów
    const lastDotIndex = numericString.lastIndexOf('.');
    const lastCommaIndex = numericString.lastIndexOf(',');

    let decimalSeparator = '.'; // Domyślnie zakładamy kropkę jako separator dziesiętny
    let thousandSeparatorRegex = /[,]/g; // Domyślnie usuwamy przecinki (jako separatory tysięcy)

    if (lastCommaIndex > lastDotIndex) {
        // Ostatni jest przecinek - traktujemy go jako dziesiętny
        decimalSeparator = ',';
        thousandSeparatorRegex = /[.]/g; // Kropki muszą być separatorami tysięcy
    } else if (lastDotIndex > lastCommaIndex) {
        // Ostatnia jest kropka - traktujemy ją jako dziesiętną
        decimalSeparator = '.';
        thousandSeparatorRegex = /[,]/g; // Przecinki muszą być separatorami tysięcy
    }
    // Przypadki z tylko jednym rodzajem separatora lub brakiem są pośrednio obsługiwane przez domyślne wartości

    // 5. Czyszczenie finalne
    // Usuń spacje (często używane jako separator tysięcy)
    numericString = numericString.replace(/\s/g, '');
    // Usuń zidentyfikowane separatory tysięcy
    numericString = numericString.replace(thousandSeparatorRegex, '');
    // Zamień zidentyfikowany separator dziesiętny (jeśli był przecinkiem) na kropkę
    if (decimalSeparator === ',') {
        numericString = numericString.replace(',', '.');
    }

    // 6. Parsowanie i formatowanie
    let floatVal = parseFloat(numericString);
    if (isNaN(floatVal)) {
        console.warn(`parsePrice: Nie udało się sparsować '${numericString}' (po czyszczeniu z '${value}')`);
        return null;
    }

    // Zwróć jako string sformatowany do 2 miejsc po przecinku
    return floatVal.toFixed(2);
}

document.getElementById("saveMapping").addEventListener("click", () => {
    let finalMappings = [];

    for (let fieldName in mappingForField) {
        let info = mappingForField[fieldName];
        if (info && info.xpath) {

            finalMappings.push({ fieldName: fieldName, localName: info.xpath });
        }
    }

    fetch(`/CeneoImportWizardXml/SaveCeneoMappings?storeId=${storeId}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(finalMappings)
    })
        .then(r => r.json())
        .then(d => {
            if (d.success !== undefined) {
                alert(d.message);
            } else {

                alert(d);
            }
        })
        .catch(err => {
            console.error("Błąd zapisu mapowań:", err);
            alert("Wystąpił błąd podczas zapisywania mapowań.");
        });
});

document.getElementById("reloadMappings").addEventListener("click", () => {
    fetch(`/CeneoImportWizardXml/GetCeneoMappings?storeId=${storeId}`)
        .then(r => r.json())
        .then(data => {
            console.log("Odebrano mapowania z bazy (reload):", data);
            clearAllHighlights();

            Object.keys(mappingForField).forEach(f => mappingForField[f] = null);

            data.forEach(m => {
                if (m.fieldName && mappingForField.hasOwnProperty(m.fieldName)) {
                    mappingForField[m.fieldName] = { xpath: m.localName, nodeCount: 0, firstValue: "" };
                }
            });

            applyExistingMappings();
        })
        .catch(err => {
            console.error("Błąd getCeneoMappings:", err);
            alert("Wystąpił błąd podczas pobierania mapowań z bazy.");
        });
});

document.getElementById("extractProducts").addEventListener("click", () => {

    if (!xmlDoc) {
        alert("Brak XML do parsowania. Sprawdź, czy plik został poprawnie załadowany.");
        return;
    }

    let entries = [];
    const possibleProductTags = ["o", "item", "entry", "produkt", "product"];
    let detectedTag = null;

    for (const tag of possibleProductTags) {
        const nodes = xmlDoc.getElementsByTagName(tag);
        if (nodes.length > 0) {
            entries = nodes;
            detectedTag = tag;
            console.log(`Wykryto tag produktu: <${detectedTag}> (${entries.length} elementów)`);
            break;
        }
    }

    if (entries.length === 0) {
        alert(`Nie znaleziono żadnego ze standardowych tagów produktu (${possibleProductTags.join(', ')}). Sprawdź strukturę XML.`);
        return;
    }

    const onlyEanProducts = document.getElementById("onlyEanProducts")?.checked ?? false;
    const removeParams = document.getElementById("cleanUrlParameters")?.checked ?? false;
    const removeDuplicateUrls = document.getElementById("removeDuplicateUrls")?.checked ?? false;
    const removeDuplicateEans = document.getElementById("removeDuplicateEans")?.checked ?? false;

    let productMaps = [];
    let countUrlsWithParams = 0;

    for (let i = 0; i < entries.length; i++) {
        let entryNode = entries[i];

        let pm = {
            StoreId: storeId,
            ExternalId: getVal(entryNode, "ExternalId"),
            Url: getVal(entryNode, "Url"),
            CeneoEan: getVal(entryNode, "CeneoEan"),
            CeneoImage: getVal(entryNode, "CeneoImage"),
            CeneoExportedName: getVal(entryNode, "CeneoExportedName"),

            CeneoXMLPrice: parsePrice(getVal(entryNode, "CeneoXMLPrice")),
            CeneoDeliveryXMLPrice: parsePrice(getVal(entryNode, "CeneoDeliveryXMLPrice"))

        };

        if (onlyEanProducts && (!pm.CeneoEan || !pm.CeneoEan.trim())) {
            continue;
        }

        if (pm.Url) {
            let qIdx = pm.Url.indexOf('?');
            if (qIdx !== -1) {
                countUrlsWithParams++;
                if (removeParams) {
                    pm.Url = pm.Url.substring(0, qIdx);
                }
            }
        }
        productMaps.push(pm);
    }

    console.log("Surowe productMaps (przed deduplikacją):", productMaps);

    if (removeDuplicateUrls) {
        let seenUrls = new Set();
        productMaps = productMaps.filter(pm => {
            if (!pm.Url || !pm.Url.trim()) return true;
            if (seenUrls.has(pm.Url)) {
                return false;
            }
            seenUrls.add(pm.Url);
            return true;
        });
    }

    if (removeDuplicateEans) {
        let seenEans = new Set();
        productMaps = productMaps.filter(pm => {
            if (!pm.CeneoEan || !pm.CeneoEan.trim()) return true;
            if (seenEans.has(pm.CeneoEan)) {
                return false;
            }
            seenEans.add(pm.CeneoEan);
            return true;
        });
    }

    console.log("Finalne productMaps (po filtrach i deduplikacji):", productMaps);

    const previewElement = document.getElementById("productMapsPreview");
    if (previewElement) {
        previewElement.textContent = JSON.stringify(productMaps, null, 2);
    }

    const urlParamsInfo = document.getElementById("urlParamsInfo");
    if (urlParamsInfo) {
        urlParamsInfo.textContent = "Liczba URL zawierających parametry: " + countUrlsWithParams;
    }
    const finalNodesInfo = document.getElementById("finalNodesInfo");
    if (finalNodesInfo) {
        finalNodesInfo.textContent = "Finalna liczba produktów po filtrach: " + productMaps.length;
    }

    checkDuplicates(productMaps);
});

function getVal(entryNode, fieldName) {
    const info = mappingForField[fieldName];
    if (!info || !info.xpath) {
        // console.log(`Brak mapowania dla pola: ${fieldName}`);
        return null;
    }

    let absolutePath = info.xpath; // np. /offers/o/name/#value lub /offers/o/@url lub /rss/channel/item/g:id/#value
    // console.log(`Pole: ${fieldName}, Węzeł: ${entryNode.nodeName}, Ścieżka absolutna: ${absolutePath}`);

    if (!entryNode || !entryNode.nodeName) {
        console.error("getVal: Nieprawidłowy entryNode");
        return null;
    }

    // --- Logika do znalezienia ścieżki względnej ---
    let entryNodePathSegments = [];
    let current = entryNode;
    // Idź w górę drzewa od entryNode, aż do korzenia dokumentu (lub gdy parentNode nie jest elementem)
    while (current && current.nodeType === Node.ELEMENT_NODE && current !== xmlDoc.documentElement.parentNode) {
        let segment = current.nodeName;
        // Dodaj predykat [@name="..."] jeśli istnieje, bo jest częścią ścieżki generowanej przez UI
        let nameAttr = current.getAttribute && current.getAttribute("name");
        if (nameAttr) {
            // Upewnij się, że escape'ujesz wartość atrybutu, jeśli może zawierać cudzysłowy
            segment += `[@name="${nameAttr.replace(/"/g, '\\"')}"]`;
        }
        entryNodePathSegments.unshift(segment); // Dodaj na początek tablicy
        current = current.parentNode;
    }
    // Zbuduj pełną ścieżkę absolutną do entryNode
    let entryNodeAbsolutePath = entryNodePathSegments.length > 0 ? "/" + entryNodePathSegments.join("/") : "";
    // console.log(`Obliczona ścieżka do entryNode: ${entryNodeAbsolutePath}`);

    let relativePath = "";

    // Normalizuj ścieżki dla porównania (małe litery, usuń końcowe /#value)
    let normalizedAbsolutePath = absolutePath.toLowerCase().replace(/\/#value$/, '');
    let normalizedEntryNodePath = entryNodeAbsolutePath.toLowerCase();

    // Przypadek 1: Ścieżka mapowania wskazuje na element potomny lub jego wartość tekstową
    // np. absolutePath = /offers/o/name/#value, entryNodeAbsolutePath = /offers/o
    // np. absolutePath = /rss/channel/item/g:id/#value, entryNodeAbsolutePath = /rss/channel/item
    if (normalizedAbsolutePath.startsWith(normalizedEntryNodePath + '/')) {
        // +1 aby usunąć wiodący '/' po ścieżce węzła
        relativePath = absolutePath.substring(entryNodeAbsolutePath.length + 1);
        // console.log(`Przypadek 1: Ścieżka względna potomka: ${relativePath}`);
    }
    // Przypadek 2: Ścieżka mapowania wskazuje na atrybut samego entryNode
    // np. absolutePath = /offers/o/@url, entryNodeAbsolutePath = /offers/o
    else if (normalizedAbsolutePath.startsWith(normalizedEntryNodePath + '/@')) {
        let lastSlashIndex = absolutePath.lastIndexOf('/');
        if (lastSlashIndex > -1 && absolutePath.substring(lastSlashIndex + 1).startsWith('@')) {
            relativePath = absolutePath.substring(lastSlashIndex + 1); // Powinno być "@url"
            // console.log(`Przypadek 2: Ścieżka względna atrybutu entryNode: ${relativePath}`);
        }
    }
    // Przypadek 3: Czasami ścieżka może nie zawierać pełnego drzewa, jeśli XML jest prosty
    // lub jeśli ścieżka została jakoś inaczej wygenerowana. Spróbuj znaleźć ostatni segment pasujący do entryNode.
    else if (absolutePath.includes('/' + entryNode.nodeName + '/')) {
        let lastNodeNameIndex = absolutePath.lastIndexOf('/' + entryNode.nodeName + '/');
        if (lastNodeNameIndex !== -1) {
            relativePath = absolutePath.substring(lastNodeNameIndex + entryNode.nodeName.length + 2); // +2 za oba '/'
            // console.log(`Przypadek 3 (fallback): Ścieżka względna po ostatnim segmencie: ${relativePath}`);
        }
    }
    // Przypadek 4: Atrybut węzła, ale ścieżka nie zawierała /@
    else if (normalizedAbsolutePath === normalizedEntryNodePath && absolutePath.includes('@')) {
        let lastSlashIndex = absolutePath.lastIndexOf('/');
        if (lastSlashIndex > -1 && absolutePath.substring(lastSlashIndex + 1).startsWith('@')) {
            relativePath = absolutePath.substring(lastSlashIndex + 1);
            // console.log(`Przypadek 4: Ścieżka względna atrybutu (alternatywnie): ${relativePath}`);
        }
    }


    if (!relativePath) {
        console.warn(`Nie można ustalić ścieżki względnej. Pole: ${fieldName}, AbsPath: ${absolutePath}, EntryNodePath: ${entryNodeAbsolutePath}`);
        // Można spróbować założyć, że zapisana ścieżka *jest* już względna (mało prawdopodobne z UI)
        // relativePath = absolutePath.startsWith('/') ? absolutePath.substring(1) : absolutePath;
        return null; // Bezpieczniej zwrócić null
    }

    // --- Koniec nowej logiki ---

    let path = relativePath; // Użyj ustalonej ścieżki względnej

    let currentNode = entryNode;
    // Rozbij ścieżkę względną na segmenty, usuwając puste (np. z //)
    const segments = path.split('/').filter(Boolean);
    // console.log(`Przetwarzanie segmentów:`, segments);

    for (let i = 0; i < segments.length; i++) {
        let seg = segments[i];

        if (!currentNode) {
            // console.log(`Zgubiono węzeł podczas przetwarzania segmentu: ${seg}`);
            return null; // Stop if we lost the node
        }

        // Jeśli segment to '#value', zwróć zawartość tekstową *aktualnego* węzła
        if (seg === '#value') {
            // Upewnij się, że currentNode to element lub tekst (chociaż tekst rzadko by tu dotarł)
            if (currentNode.nodeType === Node.ELEMENT_NODE || currentNode.nodeType === Node.TEXT_NODE) {
                // console.log(`Zwracanie textContent dla ${currentNode.nodeName}: ${currentNode.textContent?.trim()}`);
                return currentNode.textContent ? currentNode.textContent.trim() : null;
            } else {
                // console.log(`#value napotkane dla węzła typu ${currentNode.nodeType}`);
                return null; // Nie można pobrać textContent z czegoś innego
            }
        }

        // Jeśli segment zaczyna się od '@', to jest to atrybut
        if (seg.startsWith('@')) {
            const attrName = seg.substring(1);
            // Upewnij się, że currentNode jest elementem przed pobraniem atrybutu
            if (currentNode.nodeType !== Node.ELEMENT_NODE) {
                // console.log(`Próba pobrania atrybutu '${attrName}' z węzła niebędącego elementem (typ: ${currentNode.nodeType})`);
                return null;
            }
            let attrValue = currentNode.getAttribute(attrName);
            // console.log(`Pobieranie atrybutu '${attrName}' z ${currentNode.nodeName}: ${attrValue}`);
            // Zwróć wartość atrybutu (może być null, jeśli atrybut nie istnieje)
            // Jeśli to ostatni segment, zwracamy wartość. Jeśli nie, to błąd w ścieżce (nie można nawigować dalej z atrybutu)
            if (i === segments.length - 1) {
                return attrValue;
            } else {
                console.error(`Błąd ścieżki: próba nawigacji dalej po atrybucie '${attrName}'`);
                return null;
            }
        }

        // W przeciwnym razie segment musi być nazwą elementu (ewentualnie z predykatem [@name="..."])
        const bracketPos = seg.indexOf('[@name="');
        let child = null;
        let baseName = seg; // Nazwa tagu do znalezienia
        let desiredNameAttr = null; // Wartość atrybutu 'name', jeśli jest predykat

        if (bracketPos !== -1) {
            baseName = seg.substring(0, bracketPos);
            const inside = seg.substring(bracketPos + 8); // Długość '[@name="'
            const endBracket = inside.indexOf('"]');
            if (endBracket === -1) {
                console.error(`Niepoprawny format segmentu z predykatem: ${seg}`);
                return null; // Malformed segment
            }
            desiredNameAttr = inside.substring(0, endBracket);
        }

        // Znajdź pasujące dziecko elementu currentNode
        // Użyj children i nodeName - działa dobrze z i bez przestrzeni nazw
        child = Array.from(currentNode.children).find(ch => {
            // Porównaj nodeName (uwzględnia prefix np. 'g:id')
            if (ch.nodeName === baseName) {
                if (desiredNameAttr !== null) {
                    // Jeśli jest predykat [@name="..."], sprawdź atrybut 'name' dziecka
                    return ch.getAttribute("name") === desiredNameAttr;
                } else {
                    // Brak predykatu, wystarczy dopasowanie nazwy tagu
                    return true;
                }
            }
            return false; // Nazwa tagu nie pasuje
        });

        if (!child) {
            // console.log(`Segment (dziecko) nie znaleziony: '${seg}' wewnątrz ${currentNode.nodeName}`);
            return null; // Segment (dziecko) nie znaleziony
        }
        // console.log(`Znaleziono segment '${seg}', przechodzę do ${child.nodeName}`);
        currentNode = child; // Przejdź do znalezionego dziecka na potrzeby następnego segmentu
    }

    // Jeśli pętla zakończyła się normalnie, oznacza to, że ostatni segment wybrał element.
    // Zwróć jego zawartość tekstową, chyba że ścieżka kończyła się na atrybucie (co zostało obsłużone wcześniej).
    if (!segments[segments.length - 1]?.startsWith('@')) {
        // console.log(`Zwracanie textContent końcowego elementu ${currentNode.nodeName}: ${currentNode.textContent?.trim()}`);
        return currentNode.textContent ? currentNode.textContent.trim() : null;
    } else {
        // Teoretycznie nie powinno się tu zdarzyć, jeśli logika atrybutów jest poprawna
        console.warn("Ścieżka zakończyła się atrybutem, ale wartość nie została zwrócona wcześniej?");
        return null;
    }
}

document.getElementById("saveProductMapsInDb").addEventListener("click", () => {
    const previewElement = document.getElementById("productMapsPreview");
    if (!previewElement) {
        alert("Brak elementu podglądu productMapsPreview!");
        return;
    }
    const txt = previewElement.textContent.trim();
    if (!txt) {
        alert("Brak danych produktów (productMaps) do zapisania! Kliknij najpierw 'Wyciągnij Produkty'.");
        return;
    }
    let productMaps;
    try {
        productMaps = JSON.parse(txt);
    } catch (e) {
        alert("Błędny format JSON w podglądzie productMapsPreview!");
        console.error("Błąd parsowania JSON:", e, "\nTekst:", txt);
        return;
    }

    if (!Array.isArray(productMaps)) {
        alert("Dane w podglądzie nie są poprawną tablicą produktów!");
        return;
    }

    console.log("Wysyłanie productMaps do zapisu:", productMaps);

    fetch("/CeneoImportWizardXml/SaveProductMapsFromFront", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productMaps)
    })
        .then(r => r.json())
        .then(d => {

            if (d.success !== undefined) {
                alert((d.success ? "Sukces: " : "Błąd: ") + d.message);
            } else {
                alert("Otrzymano nieoczekiwaną odpowiedź z serwera.");
                console.log("Odpowiedź serwera:", d);
            }
        })
        .catch(err => {
            console.error("Błąd zapisu ProductMaps:", err);
            alert("Wystąpił błąd sieci lub serwera podczas zapisu produktów.");
        });
});

function checkDuplicates(productMaps) {
    if (!Array.isArray(productMaps)) {
        console.error("checkDuplicates: wejście nie jest tablicą");
        return;
    }
    const urlCounts = {};
    const eanCounts = {};
    let productsWithoutUrl = 0;
    let productsWithoutEan = 0;

    productMaps.forEach(pm => {
        if (pm.Url && pm.Url.trim()) {
            urlCounts[pm.Url] = (urlCounts[pm.Url] || 0) + 1;
        } else {
            productsWithoutUrl++;
        }
        if (pm.CeneoEan && pm.CeneoEan.trim()) {
            eanCounts[pm.CeneoEan] = (eanCounts[pm.CeneoEan] || 0) + 1;
        } else {
            productsWithoutEan++;
        }
    });

    const totalUniqueUrls = Object.keys(urlCounts).length;
    const duplicateUrlsCount = Object.values(urlCounts).filter(count => count > 1).length;

    const duplicateEansCount = Object.values(eanCounts).filter(count => count > 1).length;

    let urlMessage = `Unikalnych URL: ${totalUniqueUrls}`;
    if (duplicateUrlsCount > 0) {
        urlMessage += ` <span style="color:red;">(Znaleziono ${duplicateUrlsCount} zduplikowanych wartości URL)</span>`;
    } else {
        urlMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutUrl > 0) {
        urlMessage += ` | Produktów bez URL: ${productsWithoutUrl}`;
    }

    let eanMessage = `Unikalnych EAN: ${totalUniqueEans}`;
    if (duplicateEansCount > 0) {
        eanMessage += ` <span style="color:red;">(Znaleziono ${duplicateEansCount} zduplikowanych wartości EAN)</span>`;
    } else {
        eanMessage += ` (Brak duplikatów)`;
    }
    if (productsWithoutEan > 0) {
        eanMessage += ` | Produktów bez EAN: ${productsWithoutEan}`;
    }

    const duplicatesInfoElement = document.getElementById("duplicatesInfo");
    if (duplicatesInfoElement) {
        duplicatesInfoElement.innerHTML = urlMessage + "<br>" + eanMessage;
    }
}

const cleanUrlCheckbox = document.getElementById("cleanUrlParameters");
if (cleanUrlCheckbox) {
    cleanUrlCheckbox.addEventListener("change", () => {

        document.getElementById("extractProducts")?.click();
    });
}
const removeDupUrlsCheckbox = document.getElementById("removeDuplicateUrls");
if (removeDupUrlsCheckbox) {
    removeDupUrlsCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const removeDupEansCheckbox = document.getElementById("removeDuplicateEans");
if (removeDupEansCheckbox) {
    removeDupEansCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}
const onlyEanCheckbox = document.getElementById("onlyEanProducts");
if (onlyEanCheckbox) {
    onlyEanCheckbox.addEventListener("change", () => {
        document.getElementById("extractProducts")?.click();
    });
}

renderMappingTable();