// init

function setCookie(cname, cvalue) {
    const d = new Date();
    d.setTime(d.getTime() + (24*24*60*60*1000));
    let expires = "expires="+ d.toUTCString();
    document.cookie = cname + "=" + cvalue + ";" + expires + ";path=/";
} 
  
function getCookie(cname) {
    let name = cname + "=";
    let decodedCookie = decodeURIComponent(document.cookie);
    let ca = decodedCookie.split(';');
    for(let i = 0; i <ca.length; i++) {
        let c = ca[i];
        while (c.charAt(0) == ' ') {
        c = c.substring(1);
        }
        if (c.indexOf(name) == 0) {
        return c.substring(name.length, c.length);
        }
    }
return "";
}

// script

let cookieHeader = document.getElementById("gdprheader")

function renderCookieHeader() {
    if (getCookie("cookiesAccepted") == false) {
        cookieHeader.style.display = "flex"
    } else {
        cookieHeader.style.display = "none"
    }
    
}

function acceptCookies() {
    setCookie("cookiesAccepted", true)
    renderCookieHeader()
}

renderCookieHeader()