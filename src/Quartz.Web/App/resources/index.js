define(["require", "exports"], function (require, exports) {
    function configure(aurelia) {
        aurelia.globalResources("./date-format", "./duration-format");
    }
    exports.configure = configure;
});
