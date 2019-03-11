define(["require", "exports"], function (require, exports) {
    var DateFormatValueConverter = (function () {
        function DateFormatValueConverter() {
        }
        DateFormatValueConverter.prototype.toView = function (value, format) {
            if (!value) {
                return "";
            }
            return moment(value).format(format);
        };
        return DateFormatValueConverter;
    })();
    exports.DateFormatValueConverter = DateFormatValueConverter;
});
