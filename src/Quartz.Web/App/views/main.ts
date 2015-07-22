export function configure(aurelia) {
    aurelia.use
        .standardConfiguration()
        .developmentLogging()
        .plugin("./resources/index");

    aurelia.start().then(a => a.setRoot("views/app"));
}