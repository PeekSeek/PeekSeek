/* Marks the sidebar link for the page you are on.

   The page names itself through {% block currentPage %}, which layout.html writes into
   a data attribute rather than a <script> body -- a page whose name contained a quote
   used to be able to close the script tag and run as code.

   This only adds the class; what "current" looks like is default.css's job. */

document.querySelectorAll(".infoli a").forEach((link) => {
    if (link.innerText.trim().toLowerCase() === document.body.dataset.currentPage) {
        link.classList.add("currentPage");
    }
});
