codeunit 50000 MyCodeunit
{
    var
        myEnum: [|ENUM|] "My Enum";
}

table 50000 MyTable
{
    fields
    {
        field(1; "Primary Key"; Integer) { }
        field(2; myEnum; [|ENUM|] "My Enum") { }
    }
}

enum 50100 "My Enum" { value(0; "My Value") { } }
