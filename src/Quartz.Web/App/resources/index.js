define(["require", "exports"], function (require, exports) {
    function configure(aurelia) {
        aurelia.globalizeResources("./date-format", "./duration-format");
    }
    exports.configure = configure;
});
