/* ---------------  helpery  ------------------ */
function mapDayFull(d) {
    return {
        Mon: "Poniedziałek", Tue: "Wtorek", Wed: "Środa", Thu: "Czwartek",
        Fri: "Piątek", Sat: "Sobota", Sun: "Niedziela"
    }[d] || d;
}
function toggleRow(id) {
    const tr = document.getElementById(id);
    if (tr) tr.style.display = tr.style.display === "none" ? "table-row" : "none";
}
/* ----------  zmienne widoczne globalnie ---------- */
let chart;
let tooltipDates = [];     // pełna data YYYY‑MM‑DD
let tooltipDays = [];     // skrót dnia (Mon, wt., pt. itp.)

/* ---------------  Chart  -------------------- */
function drawChart(data) {
    /* 1. odświeżamy tablice dla tooltipa */
    tooltipDates = data.map(r => r.date);
    tooltipDays = data.map(r => r.day);

    /* 2. przygotowujemy serie do wykresu */
    const ctx = document.getElementById("priceAnaliseChart").getContext("2d");
    const labels = data.map(r => r.date.slice(5));   // MM‑DD
    const lowered = data.map(r => -r.lowered);
    const raised = data.map(r => r.raised);

    /* 3. jeżeli wykres już istnieje – update */
    if (chart) {
        chart.data.labels = labels;
        chart.data.datasets[0].data = lowered;
        chart.data.datasets[1].data = raised;
        chart.update();
        return;
    }

    /* 4. pierwszy raz – tworzymy wykres */
    chart = new Chart(ctx, {
        type: "bar",
        data: {
            labels,
            datasets: [
                {
                    label: "Obniżki",
                    data: lowered,
                    backgroundColor: "rgba(0,128,0,0.6)",
                    borderColor: "rgba(0,128,0,1)",
                    borderWidth: 2,
                    borderRadius: 4
                },
                {
                    label: "Podwyżki",
                    data: raised,
                    backgroundColor: "rgba(255,0,0,0.6)",
                    borderColor: "rgba(255,0,0,1)",
                    borderWidth: 2,
                    borderRadius: 4
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: "index", intersect: false },
            scales: {
                x: { stacked: true },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    ticks: { callback: v => Math.abs(v) },
                    title: { display: true, text: "Liczba zmian" }
                }
            },
            plugins: {
                legend: { display: false },
                tooltip: {
                    itemSort: (a, b) => a.dataset.label === "Podwyżki" ? -1 : 1,
                    callbacks: {
                        /* <-- korzystamy z odświeżanych tablic */
                        title: ctx => {
                            const i = ctx[0].dataIndex;
                            return `${tooltipDates[i]} (${mapDayFull(tooltipDays[i])})`;
                        },
                        label: ctx => `${ctx.dataset.label}: ${Math.abs(ctx.parsed.y)}`
                    }
                }
            }
        }
    });
}

function buildTable(rows) {
    const tb = document.querySelector(".table-orders tbody");
    tb.innerHTML = "";

    rows.forEach((r, i) => {
        const id = `det_${i}`;
        const bg = (r.day === "Sat" || r.day === "Sun") ? "#FFC0CB" : "#D3D3D3";

        /* -----------------  O B N I Ż K I  ----------------- */
        const loweredTable = r.loweredDetails.length
            ? `<table class="table table-sm">
           <thead>
             <tr><th>Produkt</th><th>Stara&nbsp;cena</th><th>Nowa&nbsp;cena</th><th>Różnica</th></tr>
           </thead>
           <tbody>
             ${r.loweredDetails.map(d => `
               <tr>
                 <td>
                   <a href="/PriceHistory/Details?productId=${d.productId}&scrapId=${d.scrapId}" target="_blank">
                     ${d.productName}
                   </a>
                 </td>
                 <td>${d.oldPrice.toFixed(2)}</td>
                 <td>${d.newPrice.toFixed(2)}</td>
                 <td style="color:green;">${d.priceDifference.toFixed(2)}</td>
               </tr>
             `).join("")}
           </tbody>
         </table>`
            : `<p>Brak obniżeń w tym dniu.</p>`;

        /* -----------------  P O D W Y Ż K I  ----------------- */
        const raisedTable = r.raisedDetails.length
            ? `<table class="table table-sm">
           <thead>
             <tr><th>Produkt</th><th>Stara&nbsp;cena</th><th>Nowa&nbsp;cena</th><th>Różnica</th></tr>
           </thead>
           <tbody>
             ${r.raisedDetails.map(d => `
               <tr>
                 <td>
                   <a href="/PriceHistory/Details?productId=${d.productId}&scrapId=${d.scrapId}" target="_blank">
                     ${d.productName}
                   </a>
                 </td>
                 <td>${d.oldPrice.toFixed(2)}</td>
                 <td>${d.newPrice.toFixed(2)}</td>
                 <td style="color:red;">+${d.priceDifference.toFixed(2)}</td>
               </tr>
             `).join("")}
           </tbody>
         </table>`
            : `<p>Brak podwyżek w tym dniu.</p>`;

        /* -----------------  wiersze tabeli  ----------------- */
        tb.insertAdjacentHTML("beforeend", `
<tr>
  <td>${r.date}<span style="background:${bg};padding:2px 5px;">${r.day}</span></td>
  <td>${r.lowered}</td>
  <td>${r.raised}</td>
  <td><button class="btn btn-sm btn-primary" onclick="toggleRow('${id}')">Rozwiń</button></td>
</tr>
<tr id="${id}" style="display:none;">
  <td colspan="4">
    <div class="row">
      <div class="col-md-6"><h4>Obniżki</h4>${loweredTable}</div>
      <div class="col-md-6"><h4>Podwyżki</h4>${raisedTable}</div>
    </div>
  </td>
</tr>`);
    });
}


/* ---------------  AJAX  --------------------- */
async function load(count) {
    const res = await fetch(`/Dashboard/GetDashboardData?storeId=${STORE_ID}&scraps=${count}`);
    if (!res.ok) { console.error("fetch error"); return; }
    const data = await res.json();
    drawChart(data);
    buildTable(data);
}

/* ---------------  init  --------------------- */
document.addEventListener("DOMContentLoaded", () => {
    load(30);
    document.querySelectorAll(".dateShortcut").forEach(btn => {
        btn.addEventListener("click", () => {
            document.querySelectorAll(".dateShortcut").forEach(b => b.classList.remove("active"));
            btn.classList.add("active");
            load(+btn.dataset.count);
        });
    });
});
























///* ════════════════  HELPERY  ════════════════ */
//function mapDayFull(d) {
//    return {
//        Mon: "Poniedziałek", Tue: "Wtorek", Wed: "Środa", Thu: "Czwartek",
//        Fri: "Piątek", Sat: "Sobota", Sun: "Niedziela",
//        "pon.": "Poniedziałek", "wt.": "Wtorek", "śr.": "Środa", "czw.": "Czwartek",
//        "pt.": "Piątek", "sob.": "Sobota", "niedz.": "Niedziela"
//    }[d] || d;
//}

///* ════════════════  ZMIENNE GLOBALNE  ════════════════ */
//let chart;                // instancja Chart.js
//let tooltipDates = [];    // pełne daty do tooltipa
//let tooltipDays = [];    // skróty dni tygodnia
//let openedId = null;      // aktualnie rozwinięty wiersz <tr class="details-row">

///* ════════════════  WYKRES  ════════════════ */
//function drawChart(data) {
//    tooltipDates = data.map(r => r.date);
//    tooltipDays = data.map(r => r.day);

//    const ctx = document.getElementById("priceAnaliseChart").getContext("2d");
//    const labels = data.map(r => r.date.slice(5));  // „MM‑DD”
//    const lowered = data.map(r => -r.lowered);
//    const raised = data.map(r => r.raised);

//    if (chart) {                    // odśwież istniejący
//        chart.data.labels = labels;
//        chart.data.datasets[0].data = lowered;
//        chart.data.datasets[1].data = raised;
//        chart.update();
//        return;
//    }

//    chart = new Chart(ctx, {
//        type: "bar",
//        data: {
//            labels,
//            datasets: [
//                {
//                    label: "Obniżki",
//                    data: lowered,
//                    backgroundColor: "rgba(0,128,0,.6)",
//                    borderColor: "rgba(0,128,0,1)",
//                    borderWidth: 2,
//                    borderRadius: 4
//                },
//                {
//                    label: "Podwyżki",
//                    data: raised,
//                    backgroundColor: "rgba(255,0,0,.6)",
//                    borderColor: "rgba(255,0,0,1)",
//                    borderWidth: 2,
//                    borderRadius: 4
//                }
//            ]
//        },
//        options: {
//            responsive: true,
//            maintainAspectRatio: false,
//            interaction: { mode: "index", intersect: false },
//            scales: {
//                x: { stacked: true },
//                y: {
//                    stacked: true,
//                    beginAtZero: true,
//                    ticks: { callback: v => Math.abs(v) },
//                    title: { display: true, text: "Liczba zmian" }
//                }
//            },
//            plugins: {
//                legend: { display: false },
//                tooltip: {
//                    itemSort: (a, b) => a.dataset.label === "Podwyżki" ? -1 : 1,
//                    callbacks: {
//                        title: c => {
//                            const i = c[0].dataIndex;
//                            return `${tooltipDates[i]} (${mapDayFull(tooltipDays[i])})`;
//                        },
//                        label: c => `${c.dataset.label}: ${Math.abs(c.parsed.y)}`
//                    }
//                }
//            }
//        }
//    });
//}

///* ════════════════  TABELA  ════════════════ */
//function buildTable(rows) {
//    const tb = document.querySelector(".table-orders tbody");
//    tb.innerHTML = "";

//    rows.forEach((r, i) => {
//        const detailId = `detail_${i}`;
//        const weekend = r.day === "Sat" || r.day === "Sun" || r.day === "sob." || r.day === "niedz.";
//        const dayBg = weekend ? "#FFC0CB" : "#D3D3D3";

//        /* --- HTML obniżek --- */
//        const loweredTable = r.loweredDetails.length
//            ? `<table class="table table-sm"><thead>
//                 <tr><th>Produkt</th><th>Stara</th><th>Nowa</th><th>Δ</th></tr>
//             </thead><tbody>${r.loweredDetails.map(d => `
//                    <tr>
//                      <td><a href="/PriceHistory/Details?productId=${d.productId}&scrapId=${d.scrapId}"
//                             target="_blank">${d.productName}</a></td>
//                      <td>${d.oldPrice.toFixed(2)}</td>
//                      <td>${d.newPrice.toFixed(2)}</td>
//                      <td style="color:green;">${d.priceDifference.toFixed(2)}</td>
//                    </tr>`).join("")}
//             </tbody></table>`
//            : "<p>Brak obniżeń w tym dniu.</p>";

//        /* --- HTML podwyżek --- */
//        const raisedTable = r.raisedDetails.length
//            ? `<table class="table table-sm"><thead>
//                 <tr><th>Produkt</th><th>Stara</th><th>Nowa</th><th>Δ</th></tr>
//             </thead><tbody>${r.raisedDetails.map(d => `
//                    <tr>
//                      <td><a href="/PriceHistory/Details?productId=${d.productId}&scrapId=${d.scrapId}"
//                             target="_blank">${d.productName}</a></td>
//                      <td>${d.oldPrice.toFixed(2)}</td>
//                      <td>${d.newPrice.toFixed(2)}</td>
//                      <td style="color:red;">+${d.priceDifference.toFixed(2)}</td>
//                    </tr>`).join("")}
//             </tbody></table>`
//            : "<p>Brak podwyżek w tym dniu.</p>";

//        /* --- wstawiamy dwa wiersze --- */
//        tb.insertAdjacentHTML("beforeend", `
//<tr class="parent-row" data-target="${detailId}">
//  <td>${r.date}<span style="background:${dayBg};padding:2px 5px;">${r.day}</span></td>
//  <td>${r.lowered}</td>
//  <td>${r.raised}</td>
//  <td>↧</td>
//</tr>
//<tr id="${detailId}" class="details-row">
//  <td colspan="4">
//     <div class="row">
//       <div class="col-md-6"><h4>Obniżki</h4>${loweredTable}</div>
//       <div class="col-md-6"><h4>Podwyżki</h4>${raisedTable}</div>
//     </div>
//  </td>
//</tr>`);
//    });

//    /* jedno wspólne zdarzenie */
//    tb.querySelectorAll("tr.parent-row").forEach(tr => {
//        tr.addEventListener("click", () => toggleRowSmooth(tr.dataset.target));
//    });
//}

///* ════════════════  ANIMACJA WYIERSZY  ════════════════ */
//function toggleRowSmooth(id) {
//    if (openedId && openedId !== id) closeRow(openedId);     // zwijamy poprzedni
//    if (openedId === id) { closeRow(id); openedId = null; return; }

//    const row = document.getElementById(id);
//    if (!row) return;

//    row.classList.add("open");
//    row.style.height = row.scrollHeight + "px";
//    openedId = id;
//}
//function closeRow(id) {
//    const row = document.getElementById(id);
//    if (!row) return;

//    row.style.height = row.scrollHeight + "px"; // start
//    row.offsetHeight;                         // reflow
//    row.style.height = "0px";

//    row.addEventListener("transitionend", function h() {
//        row.classList.remove("open");
//        row.style.height = "";
//        row.removeEventListener("transitionend", h);
//    });
//}

///* ════════════════  AJAX  ════════════════ */
//async function load(count) {
//    const res = await fetch(`/Dashboard/GetDashboardData?storeId=${STORE_ID}&scraps=${count}`);
//    if (!res.ok) { console.error("fetch error"); return; }
//    const data = await res.json();
//    drawChart(data);
//    buildTable(data);
//}

///* ════════════════  INIT  ════════════════ */
//document.addEventListener("DOMContentLoaded", () => {
//    load(30);                                         // domyślnie 30 scrapów
//    document.querySelectorAll(".dateShortcut").forEach(btn => {
//        btn.addEventListener("click", () => {
//            document.querySelectorAll(".dateShortcut").forEach(b => b.classList.remove("active"));
//            btn.classList.add("active");
//            load(+btn.dataset.count);
//        });
//    });
//});
