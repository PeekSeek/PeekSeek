/* Rewrites every <element class="date" data-epoch="..."> into the reader's own timezone.

   Event times are sent as epoch seconds and formatted here rather than on the server,
   because an event that opens at 20:00 CET has to read as 20:00 to someone in Paris and
   as 14:00 to someone in New York, and the server has no idea which of them is asking. */

document.querySelectorAll(".date").forEach((element) => {
    const date = new Date(parseInt(element.dataset.epoch, 10) * 1000);

    const formatted = date.toLocaleString(undefined, {
        day: "numeric",
        month: "short",
        hour: "numeric",
    });

    element.textContent = formatted.replace(",", "").replace(" PM", "PM").replace(" AM", "AM");
});
