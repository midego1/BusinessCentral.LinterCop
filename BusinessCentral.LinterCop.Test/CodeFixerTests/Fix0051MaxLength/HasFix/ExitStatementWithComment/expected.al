codeunit 50100 MyCodeunit
{
    procedure MyProcedure(): Text[10]
    var
        MyLabelLbl: Label 'My Label', Comment = 'This is a label for demonstration', MaxLength = 10;
    begin
        exit(MyLabelLbl);
    end;
}