document.querySelectorAll(".stat-card").forEach((card, index) => {
    card.animate(
        [{ transform: "translateY(15px)", opacity: 0 }, { transform: "translateY(0)", opacity: 1 }],
        { duration: 500 + index * 80, fill: "forwards", easing: "ease-out" }
    );
});
