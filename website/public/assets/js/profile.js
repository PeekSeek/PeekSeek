// The Elo chart on a profile. The series arrives as JSON in #eloHistory, the way every
// other handoff on the site works, so none of this configuration lives in the template.

const series = JSON.parse(document.getElementById("eloHistory").textContent);
const line = "#ff4444";
const grid = "#1a1a1a";
const label = "#8a8a8a";

new Chart(document.getElementById("eloChart"), {
  type: "line",
  data: {
    labels: series.map(point => point.date),
    datasets: [{
      data: series.map(point => point.elo),
      borderColor: line,
      backgroundColor: "rgba(255,68,68,0.08)",
      fill: true,
      tension: 0.3,
      pointRadius: 2,
      pointBackgroundColor: line,
      pointBorderColor: line,
    }],
  },
  options: {
    maintainAspectRatio: false,
    plugins: { legend: { display: false } },
    scales: {
      x: { grid: { color: grid }, ticks: { color: label, font: { size: 11 } } },
      y: { grid: { color: grid }, ticks: { color: label, font: { size: 11 } } },
    },
  },
});
