// This file is for your custom client-side JavaScript.
// You can add interactivity to your pages here.

// Wait for the DOM to be fully loaded before running any scripts
document.addEventListener('DOMContentLoaded', function () {

    // --- Mobile Menu Toggle ---
    // This script handles the functionality for the hamburger menu on mobile devices.

    // Find the mobile menu button and the navigation menu itself.
    // Note: The mobile menu structure will need a slight adjustment in _Layout.cshtml to work with this.
    const mobileMenuButton = document.getElementById('mobile-menu-button');
    const mobileMenu = document.getElementById('mobile-menu');

    if (mobileMenuButton && mobileMenu) {
        // Add a click event listener to the button.
        mobileMenuButton.addEventListener('click', function () {
            // Toggle the 'hidden' class on the menu to show or hide it.
            mobileMenu.classList.toggle('hidden');
        });
    }

});
