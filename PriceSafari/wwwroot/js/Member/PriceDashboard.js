/* =====================  Helpery  ===================== */
function toggleDetails(id) {
    const el = document.getElementById(id);
    if (el) el.style.display = (el.style.display === "none") ? "table-row" : "none";
}

function mapDayFull(engAbbrev) {
    switch (engAbbrev) {
        case "Mon": return "Poniedziałek";
        case "Tue": return "Wtorek";
        case "Wed": return "Środa";
        case "Thu": return "Czwartek";
        case "Fri": return "Piątek";
        case "Sat": return "Sobota";
        case "Sun": return "Niedziela";
        default: return engAbbrev;
    }
}

/* =====================  Wykres  ===================== */
document.addEventListener("DOMContentLoaded", () => {

    /* 1. JSON z widoku -------------------------------------------------- */
    const raw = document.getElementById("chart-data")?.textContent;
    if (!raw) return;

    const rows = JSON.parse(raw);
    if (!rows.length) return;

    /* 2. Dane dla Chart.js --------------------------------------------- */
    const fullDates = rows.map(r => r.date);              // 2025‑04‑17
    const chartLabels = rows.map(r => r.date.slice(5));     // 04‑17
    const dayAbbr = rows.map(r => r.day);               // Mon, Tue …
    const raisedData = rows.map(r => r.raised);
    const loweredData = rows.map(r => -r.lowered);

    /* 3. Rysowanie ------------------------------------------------------ */
    const canvas = document.getElementById("priceAnaliseChart");
    if (!canvas) return;

    new Chart(canvas.getContext("2d"), {
        type: "bar",
        data: {
            labels: chartLabels,
            datasets: [
                {
                    label: "Obniżki",
                    data: loweredData,
                    backgroundColor: "rgba(0,128,0,0.6)",
                    borderColor: "rgba(0,128,0,1)",
                    borderWidth: 2,
                    borderRadius: 4
                },
                {
                    label: "Podwyżki",
                    data: raisedData,
                    backgroundColor: "rgba(255,0,0,0.6)",
                    borderColor: "rgba(255,0,0,1)",
                    borderWidth: 2,
                    borderRadius: 4
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,      // 🔄 rozciągaj / kurcz bez zachowania proporcji
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
                legend: { display: false },   // ⛔ brak legendy
                tooltip: {
                    itemSort: (a, b) => a.dataset.label === "Podwyżki" ? -1 : 1,
                    callbacks: {
                        title: ctx => {
                            const i = ctx[0].dataIndex;
                            return `${fullDates[i]} (${mapDayFull(dayAbbr[i])})`;
                        },
                        label: ctx =>
                            `${ctx.dataset.label}: ${Math.abs(ctx.parsed.y)}`
                    }
                }
            }
        }
    });
});
