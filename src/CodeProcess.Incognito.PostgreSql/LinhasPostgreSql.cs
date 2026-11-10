using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Unicode;
using CodeProcess.Incognito.Transformadores;
using Npgsql;

namespace CodeProcess.Incognito.Dialetos.PostgreSql;

/// <summary>
/// O texto de um campo do <c>COPY</c> com os parâmetros que o Incognito impõe (<see cref="ConexaoPostgreSql.OpcoesDoTexto"/>):
/// o desescape e a conversão para a forma canônica de cada <see cref="TipoColuna"/>, sempre por <c>TryParse</c> sobre o
/// próprio span, nunca por exceção nem por uma <c>string</c> intermediária com o valor.
/// </summary>
internal static class TextoDoCopy
{
    // O maior texto de número, data, hora ou identificador que se converte; acima disso, não tem forma canônica.
    private const int MaiorTextoCurto = 64;

    private static readonly string[] _formatosDataHora = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFF"];

    /// <summary>Se o campo é o NULL do <c>COPY</c> (<c>\N</c>).</summary>
    /// <param name="campo">O campo como veio.</param>
    /// <returns>Se é NULL.</returns>
    internal static bool Nulo(ReadOnlySpan<byte> campo) => campo is [(byte)'\\', (byte)'N'];

    /// <summary>
    /// Desescapa um campo do <c>COPY</c> em texto: as sequências que o <c>COPY TO</c> emite (<c>\b \f \n \r \t \v \\</c>) e,
    /// por segurança, as octais e hexadecimais da entrada; outra letra depois da barra fica como ela mesma.
    /// </summary>
    /// <param name="campo">O campo, que não é NULL.</param>
    /// <param name="destino">Onde escrever; no máximo do tamanho do campo.</param>
    /// <returns>Quantos bytes foram escritos.</returns>
    internal static int Desescapar(ReadOnlySpan<byte> campo, Span<byte> destino)
    {
        var escritos = 0;
        for (var i = 0; i < campo.Length; i++)
        {
            var atual = campo[i];
            if (atual != (byte)'\\' || i + 1 == campo.Length)
            {
                destino[escritos++] = atual;
                continue;
            }

            var seguinte = campo[++i];
            switch (seguinte)
            {
                case (byte)'b': destino[escritos++] = 0x08; break;
                case (byte)'f': destino[escritos++] = 0x0C; break;
                case (byte)'n': destino[escritos++] = 0x0A; break;
                case (byte)'r': destino[escritos++] = 0x0D; break;
                case (byte)'t': destino[escritos++] = 0x09; break;
                case (byte)'v': destino[escritos++] = 0x0B; break;
                case >= (byte)'0' and <= (byte)'7':
                    {
                        var valor = seguinte - '0';
                        for (var digitos = 1; digitos < 3 && i + 1 < campo.Length && campo[i + 1] is >= (byte)'0' and <= (byte)'7'; digitos++)
                        {
                            valor = (valor * 8) + (campo[++i] - '0');
                        }

                        destino[escritos++] = (byte)valor;
                        break;
                    }

                case (byte)'x' when i + 1 < campo.Length && Hexa(campo[i + 1]) >= 0:
                    {
                        var valor = Hexa(campo[++i]);
                        if (i + 1 < campo.Length && Hexa(campo[i + 1]) >= 0)
                        {
                            valor = (valor * 16) + Hexa(campo[++i]);
                        }

                        destino[escritos++] = (byte)valor;
                        break;
                    }

                default:
                    destino[escritos++] = seguinte;
                    break;
            }
        }

        return escritos;
    }

    /// <summary>
    /// O <c>bytea</c> direto do campo bruto do <c>COPY</c> (<c>\\x</c> seguido de hexadecimal), sem passar pelo desescape:
    /// uma cópia a menos do valor. Falso quando o campo não tem essa forma.
    /// </summary>
    /// <param name="campo">O campo como veio, que não é NULL.</param>
    /// <param name="valor">Os bytes, ou nulo quando falso.</param>
    /// <returns>Se converteu.</returns>
    internal static bool TentarBinarioBruto(ReadOnlySpan<byte> campo, out object? valor)
    {
        valor = null;
        if (campo is not [(byte)'\\', (byte)'\\', (byte)'x', ..] || !TentarHexa(campo[3..], out var bytes))
        {
            return false;
        }

        valor = bytes;
        return true;
    }

    /// <summary>
    /// Converte o texto desescapado para a forma canônica do tipo. Falso quando o valor não tem representação nela (data
    /// <c>infinity</c>, antes de Cristo ou depois de 9999; <c>numeric</c> <c>NaN</c>, infinito, ou com mais dígitos do que o
    /// <c>decimal</c> guarda; texto que não é UTF-8). O <c>decimal</c> guarda a escala do banco.
    /// </summary>
    /// <param name="tipo">O tipo canônico.</param>
    /// <param name="texto">O texto, já desescapado.</param>
    /// <param name="valor">O valor, ou nulo quando falso.</param>
    /// <returns>Se converteu.</returns>
    internal static bool TentarConverter(TipoColuna tipo, ReadOnlySpan<byte> texto, out object? valor)
    {
        valor = null;
        if (tipo == TipoColuna.Texto)
        {
            // Validado antes, sem exceção: a de decodificação levaria os bytes do valor.
            if (!Utf8.IsValid(texto))
            {
                return false;
            }

            valor = Encoding.UTF8.GetString(texto);
            return true;
        }

        if (tipo == TipoColuna.Binario)
        {
            if (!texto.StartsWith("\\x"u8) || !TentarHexa(texto[2..], out var bytes))
            {
                return false;
            }

            valor = bytes;
            return true;
        }

        Span<char> caracteres = stackalloc char[MaiorTextoCurto];
        if (texto.Length > MaiorTextoCurto)
        {
            return false;
        }

        for (var i = 0; i < texto.Length; i++)
        {
            caracteres[i] = (char)texto[i];
        }

        var curto = caracteres[..texto.Length];
        try
        {
            switch (tipo)
            {
                case TipoColuna.Inteiro when long.TryParse(texto, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var inteiro):
                    valor = inteiro;
                    return true;
                case TipoColuna.Decimal when TentarDecimal(texto, out var numero):
                    valor = numero;
                    return true;
                case TipoColuna.Booleano when texto is [(byte)'t'] or [(byte)'f']:
                    valor = texto[0] == (byte)'t';
                    return true;
                case TipoColuna.Data when DateOnly.TryParseExact(curto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data):
                    valor = data;
                    return true;
                case TipoColuna.DataHora when DateTime.TryParseExact(curto, _formatosDataHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dataHora):
                    valor = dataHora;
                    return true;
                case TipoColuna.DataHoraComFuso when curto.EndsWith("+00", StringComparison.Ordinal)
                    && DateTime.TryParseExact(curto[..^3], _formatosDataHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var noUtc):
                    valor = new DateTimeOffset(noUtc, TimeSpan.Zero);
                    return true;
                case TipoColuna.Uuid when Guid.TryParseExact(curto, "D", out var guid):
                    valor = guid;
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            caracteres.Clear();
        }
    }

    // O decimal arredonda acima de 28 dígitos sem falhar: só vale quando a ida e volta reproduz o texto (a escala é guardada).
    private static bool TentarDecimal(ReadOnlySpan<byte> texto, out decimal valor)
    {
        if (!decimal.TryParse(texto, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor))
        {
            return false;
        }

        Span<byte> devolta = stackalloc byte[MaiorTextoCurto];
        var igual = valor.TryFormat(devolta, out var escritos, default, CultureInfo.InvariantCulture) && devolta[..escritos].SequenceEqual(texto);
        devolta.Clear();
        return igual;
    }

    private static bool TentarHexa(ReadOnlySpan<byte> texto, out byte[] valor)
    {
        valor = [];
        if (texto.Length % 2 != 0)
        {
            return false;
        }

        var bytes = new byte[texto.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var alto = Hexa(texto[2 * i]);
            var baixo = Hexa(texto[(2 * i) + 1]);
            if (alto < 0 || baixo < 0)
            {
                // O que já foi decodificado é do valor: some antes de virar lixo.
                Array.Clear(bytes);
                return false;
            }

            bytes[i] = (byte)((alto * 16) + baixo);
        }

        valor = bytes;
        return true;
    }

    private static int Hexa(byte caractere) => caractere switch
    {
        >= (byte)'0' and <= (byte)'9' => caractere - '0',
        >= (byte)'a' and <= (byte)'f' => caractere - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => caractere - 'A' + 10,
        _ => -1,
    };
}

/// <summary>Os buffers de bytes com valores: sempre limpos no trecho usado antes de voltar ao pool, qualquer que seja o tamanho.</summary>
internal static class Buffers
{
    /// <summary>
    /// Limpa o trecho usado e devolve o buffer ao pool. O <c>clearArray</c> do pool não basta: acima do maior tamanho que ele
    /// guarda, o array é solto sujo.
    /// </summary>
    /// <param name="pool">O pool.</param>
    /// <param name="buffer">O buffer.</param>
    /// <param name="usados">Quantos bytes do começo foram escritos.</param>
    internal static void Devolver(ArrayPool<byte> pool, byte[] buffer, int usados)
    {
        buffer.AsSpan(0, Math.Min(usados, buffer.Length)).Clear();
        pool.Return(buffer);
    }
}

/// <summary>
/// O corte do fluxo do <c>COPY</c> em lotes de linhas: até <see cref="LinhasPorLote"/> linhas ou <see cref="BytesPorLote"/>
/// bytes; o lote fecha antes de o buffer crescer, e só uma linha maior que o lote o faz crescer, sozinha. O que passou do
/// lote fica para o seguinte. Sem banco: o fluxo é uma função de leitura.
/// </summary>
internal sealed class CortadorDeLinhas(ArrayPool<byte> pool)
{
    /// <summary>O máximo de linhas de um lote.</summary>
    internal const int LinhasPorLote = 1_000;

    /// <summary>O tamanho de um lote; uma linha maior que isso vai sozinha num lote do tamanho dela.</summary>
    internal const int BytesPorLote = 256 * 1024;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private byte[]? _resto;
    private int _restoUsado;
    private bool _fimDoFluxo;

    /// <summary>Se o fluxo já chegou ao fim (o que falta está no resto).</summary>
    internal bool FimDoFluxo => _fimDoFluxo;

    /// <summary>O próximo lote, ou nulo quando o fluxo acabou sem mais linhas.</summary>
    /// <param name="ler">Lê do fluxo para o espaço dado; zero no fim.</param>
    /// <returns>Os bytes (do pool, com as linhas a partir do zero), o início de cada linha mais o fim da última, e quantas.</returns>
    /// <exception cref="IncognitoException">O fluxo terminou no meio de uma linha.</exception>
    internal async Task<(byte[] Dados, int[] Inicios, int Linhas)?> ProximoAsync(Func<Memory<byte>, ValueTask<int>> ler)
    {
        if (_fimDoFluxo && _restoUsado == 0)
        {
            return null;
        }

        var dados = _resto ?? pool.Rent(BytesPorLote);
        var usados = _restoUsado;
        _resto = null;
        _restoUsado = 0;
        var inicios = ArrayPool<int>.Shared.Rent(LinhasPorLote + 1);
        var linhas = 0;
        var fimDasLinhas = 0;
        var varridos = 0;
        try
        {
            while (true)
            {
                while (linhas < LinhasPorLote && fimDasLinhas < BytesPorLote)
                {
                    var quebra = dados.AsSpan(varridos, usados - varridos).IndexOf((byte)'\n');
                    if (quebra < 0)
                    {
                        varridos = usados;
                        break;
                    }

                    inicios[linhas++] = fimDasLinhas;
                    fimDasLinhas = varridos + quebra + 1;
                    varridos = fimDasLinhas;
                }

                if (linhas == LinhasPorLote || (linhas > 0 && fimDasLinhas >= BytesPorLote) || _fimDoFluxo)
                {
                    break;
                }

                if (usados == dados.Length)
                {
                    // Cheio no meio de uma linha: com linhas inteiras, o lote fecha, e a linha parcial vai para o seguinte;
                    // sem nenhuma, a linha é maior que o buffer, e ele cresce.
                    if (linhas > 0)
                    {
                        break;
                    }

                    var maior = pool.Rent(dados.Length * 2);
                    dados.AsSpan(0, usados).CopyTo(maior);
                    Buffers.Devolver(pool, dados, usados);
                    dados = maior;
                }

                var lidos = await ler(dados.AsMemory(usados)).ConfigureAwait(false);
                if (lidos == 0)
                {
                    _fimDoFluxo = true;
                    if (usados > fimDasLinhas)
                    {
                        throw new IncognitoException(CodigosPostgreSql.LinhasNaoLidas, "O COPY do PostgreSQL terminou no meio de uma linha.");
                    }
                }

                usados += lidos;
            }

            // O que passou do lote vai para o próximo, e sai deste buffer.
            if (usados > fimDasLinhas)
            {
                _restoUsado = usados - fimDasLinhas;
                _resto = pool.Rent(Math.Max(BytesPorLote, _restoUsado));
                dados.AsSpan(fimDasLinhas, _restoUsado).CopyTo(_resto);
                dados.AsSpan(fimDasLinhas, _restoUsado).Clear();
                usados = fimDasLinhas;
            }

            if (linhas == 0)
            {
                return null;
            }

            inicios[linhas] = fimDasLinhas;
            var lote = (dados, inicios, linhas);
            dados = null!;
            inicios = null!;
            return lote;
        }
        finally
        {
            if (dados is not null)
            {
                Buffers.Devolver(pool, dados, usados);
            }

            if (inicios is not null)
            {
                ArrayPool<int>.Shared.Return(inicios);
            }
        }
    }

    /// <summary>Limpa e devolve o resto, uma vez.</summary>
    internal void Limpar()
    {
        if (_resto is not null)
        {
            Buffers.Devolver(pool, _resto, _restoUsado);
            _resto = null;
            _restoUsado = 0;
        }
    }
}

/// <summary>
/// Um lote do PostgreSQL: as linhas do <c>COPY</c> em bytes crus, num buffer do pool da sessão, com o início de cada linha e,
/// quando há convertidas, o de cada campo. A escrita guarda o valor tratado e apaga os bytes originais da célula; o
/// descarte limpa os buffers ao devolvê-los.
/// </summary>
internal sealed class LoteLinhasPostgreSql : LoteLinhas
{
    private readonly ArrayPool<byte> _pool;
    private readonly int _colunas;
    private readonly int _convertidas;
    private readonly TipoColuna?[] _tipos;
    private readonly int[] _convertida;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private byte[] _dados;
    private int[] _inicios;
    private int[]? _campos;
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private object?[]? _escritos;
    private bool[]? _foiEscrita;

    /// <summary>Monta o lote sobre as linhas já separadas; o lote passa a ser o dono dos buffers.</summary>
    /// <param name="pedido">O pedido da leitura.</param>
    /// <param name="dados">Os bytes das linhas, do pool.</param>
    /// <param name="inicios">O início de cada linha e, por último, o fim da última (depois da quebra), do pool.</param>
    /// <param name="quantidade">Quantas linhas.</param>
    /// <param name="pool">O pool da sessão.</param>
    internal LoteLinhasPostgreSql(PedidoLeituraTabela pedido, byte[] dados, int[] inicios, int quantidade, ArrayPool<byte> pool)
        : base(pedido, quantidade)
    {
        _pool = pool;
        _dados = dados;
        _inicios = inicios;
        _colunas = pedido.Colunas.Count;
        _convertidas = pedido.Convertidas.Count;
        _tipos = new TipoColuna?[_colunas];
        _convertida = new int[_colunas];
        Array.Fill(_convertida, -1);
        for (var i = 0; i < pedido.Convertidas.Count; i++)
        {
            _tipos[pedido.Convertidas[i].Indice] = pedido.Convertidas[i].Tipo;
            _convertida[pedido.Convertidas[i].Indice] = i;
        }

        if (pedido.Convertidas.Count > 0)
        {
            Dividir();
        }
    }

    /// <summary>Os bytes de uma linha, sem a quebra do fim.</summary>
    /// <param name="linha">A linha.</param>
    /// <returns>Os bytes.</returns>
    internal ReadOnlySpan<byte> Linha(int linha) => _dados.AsSpan(_inicios[linha], _inicios[linha + 1] - _inicios[linha] - 1);

    /// <summary>Quantas colunas cada linha tem.</summary>
    internal int Colunas => _colunas;

    /// <summary>Se alguma célula da linha foi escrita (a mantida não conta: vai como veio).</summary>
    /// <param name="linha">A linha.</param>
    /// <returns>Se há célula escrita.</returns>
    internal bool TemEscrita(int linha) => _foiEscrita is { } escritas && escritas.AsSpan(linha * _convertidas, _convertidas).Contains(true);

    /// <summary>O valor escrito numa célula, quando foi escrita.</summary>
    /// <param name="linha">A linha.</param>
    /// <param name="coluna">A coluna; a não convertida nunca é escrita.</param>
    /// <param name="valor">O valor escrito, na forma canônica, ou nulo.</param>
    /// <returns>Se a célula foi escrita.</returns>
    internal bool Escrito(int linha, int coluna, out object? valor)
    {
        var convertida = _convertida[coluna];
        if (convertida < 0 || _foiEscrita is not { } escritas || !escritas[(linha * _convertidas) + convertida])
        {
            valor = null;
            return false;
        }

        valor = _escritos![(linha * _convertidas) + convertida];
        return true;
    }

    /// <summary>Os bytes de um campo como vieram do <c>COPY</c> (só num lote com convertidas, que divide as linhas).</summary>
    /// <param name="linha">A linha.</param>
    /// <param name="coluna">A coluna.</param>
    /// <returns>Os bytes.</returns>
    internal ReadOnlySpan<byte> CampoBruto(int linha, int coluna) => Campo(linha, coluna);

    protected override bool TentarLerCore(int linha, int coluna, out object? valor)
    {
        var campo = Campo(linha, coluna);
        if (TextoDoCopy.Nulo(campo))
        {
            valor = null;
            return true;
        }

        var tipo = _tipos[coluna]!.Value;
        if (tipo == TipoColuna.Binario)
        {
            return TextoDoCopy.TentarBinarioBruto(campo, out valor);
        }

        if (campo.IndexOf((byte)'\\') < 0)
        {
            return TextoDoCopy.TentarConverter(tipo, campo, out valor);
        }

        var temporario = _pool.Rent(campo.Length);
        var escritos = 0;
        try
        {
            escritos = TextoDoCopy.Desescapar(campo, temporario);
            return TextoDoCopy.TentarConverter(tipo, temporario.AsSpan(0, escritos), out valor);
        }
        finally
        {
            Buffers.Devolver(_pool, temporario, escritos);
        }
    }

    protected override void EscreverCore(int linha, int coluna, object? valor)
    {
        _escritos ??= new object?[Quantidade * _convertidas];
        _foiEscrita ??= new bool[Quantidade * _convertidas];
        _escritos[(linha * _convertidas) + _convertida[coluna]] = valor;
        _foiEscrita[(linha * _convertidas) + _convertida[coluna]] = true;

        // O original da célula escrita não é mais lido nem gravado: some do buffer.
        _dados.AsSpan(Inicio(linha, coluna), Campo(linha, coluna).Length).Clear();
    }

    protected override void ManterCore(int linha, int coluna)
    {
        // O original fica no buffer e é gravado como veio.
    }

    protected override void DisposeCore()
    {
        Buffers.Devolver(_pool, _dados, _inicios.Length > Quantidade ? _inicios[Quantidade] : _dados.Length);
        ArrayPool<int>.Shared.Return(_inicios);
        if (_campos is not null)
        {
            ArrayPool<int>.Shared.Return(_campos);
        }

        if (_escritos is not null)
        {
            // O binário escrito é a cópia que a base fez: zerado aqui; o texto não se zera (INC-044).
            foreach (var escrito in _escritos)
            {
                if (escrito is byte[] bytes)
                {
                    Array.Clear(bytes);
                }
            }

            Array.Clear(_escritos);
        }

        _dados = [];
        _inicios = [];
        _campos = null;
        _escritos = null;
        _foiEscrita = null;
    }

    // As posições dos campos de cada linha: o início de cada um e, por último, o fim da linha mais um (como se houvesse um
    // separador). Uma linha com outro número de campos é defeito: o COPY TO escapa a tabulação dentro do valor.
    private void Dividir()
    {
        var campos = ArrayPool<int>.Shared.Rent(Quantidade * (_colunas + 1));
        try
        {
            for (var linha = 0; linha < Quantidade; linha++)
            {
                var inicio = _inicios[linha];
                var fim = _inicios[linha + 1] - 1;
                var baseDaLinha = linha * (_colunas + 1);
                campos[baseDaLinha] = inicio;
                var campo = 1;
                var posicao = inicio;
                while (true)
                {
                    var tabulacao = _dados.AsSpan(posicao, fim - posicao).IndexOf((byte)'\t');
                    if (tabulacao < 0)
                    {
                        break;
                    }

                    if (campo == _colunas)
                    {
                        throw Forma();
                    }

                    posicao += tabulacao + 1;
                    campos[baseDaLinha + campo++] = posicao;
                }

                if (campo != _colunas)
                {
                    throw Forma();
                }

                campos[baseDaLinha + _colunas] = fim + 1;
            }
        }
        catch
        {
            ArrayPool<int>.Shared.Return(campos);
            throw;
        }

        _campos = campos;
    }

    private int Inicio(int linha, int coluna) => _campos![(linha * (_colunas + 1)) + coluna];

    private ReadOnlySpan<byte> Campo(int linha, int coluna)
    {
        var inicio = Inicio(linha, coluna);
        return _dados.AsSpan(inicio, _campos![(linha * (_colunas + 1)) + coluna + 1] - 1 - inicio);
    }

    private static IncognitoException Forma() => new(
        CodigosPostgreSql.LinhasNaoLidas,
        "O COPY do PostgreSQL devolveu uma linha com um número de campos diferente do pedido.");
}

/// <summary>
/// A leitura das linhas de uma tabela no PostgreSQL: o <c>COPY ... TO STDOUT</c> em texto, num ponto de salvamento aberto
/// pelo leitor, lido em bytes crus pela <see cref="StreamReader.BaseStream"/> (o <c>TextReader</c> trocaria bytes), cortado
/// em lotes pelo <see cref="CortadorDeLinhas"/>. O token do chamador nunca vai ao fluxo do <c>COPY</c> (deixaria a conexão
/// inutilizável): o cancelamento é em dois estágios, o pedido ao servidor (<c>Cancel()</c>, fora da thread de quem
/// cancela) e, se ele não responde em <see cref="PrazoDoCancelamento"/>, a desistência da conexão.
/// </summary>
internal sealed class LeituraTabelaPostgreSql : LeituraTabela
{
    /// <summary>Quanto esperar o servidor responder ao cancelamento ou ao descarte antes de desistir da conexão.</summary>
    internal static readonly TimeSpan PrazoDoCancelamento = TimeSpan.FromSeconds(10);

    private readonly NpgsqlConnection _conexao;
    private readonly StreamReader _exportacao;
    private readonly NpgsqlRawCopyStream _fluxo;
    private readonly ArrayPool<byte> _pool;
    private readonly CortadorDeLinhas _cortador;
    private readonly bool _emReplica;
    private readonly string _ponto;
    private readonly string _tabela;
    private readonly Func<NpgsqlConnection, CancellationToken, Task>? _antesDaConclusao;
    private readonly Action<LeituraTabelaPostgreSql> _aoDescartar;
    private bool _concluida;

    internal LeituraTabelaPostgreSql(
        NpgsqlConnection conexao,
        StreamReader exportacao,
        ArrayPool<byte> pool,
        bool emReplica,
        string ponto,
        string tabela,
        Func<NpgsqlConnection, CancellationToken, Task>? antesDaConclusao,
        Action<LeituraTabelaPostgreSql> aoDescartar)
    {
        _conexao = conexao;
        _exportacao = exportacao;
        _fluxo = (NpgsqlRawCopyStream)exportacao.BaseStream;
        _pool = pool;
        _cortador = new CortadorDeLinhas(pool);
        _emReplica = emReplica;
        _ponto = ponto;
        _tabela = tabela;
        _antesDaConclusao = antesDaConclusao;
        _aoDescartar = aoDescartar;
    }

    protected override async Task<LoteLinhas?> LerLoteAsyncCore(CancellationToken cancelamento)
    {
        try
        {
            using var desistir = new CancellationTokenSource();
            (byte[] Dados, int[] Inicios, int Linhas)? cortado;
            using (cancelamento.Register(static estado => ((LeituraTabelaPostgreSql)estado!).PedirCancelamento(), this))
            using (cancelamento.Register(static estado => ((CancellationTokenSource)estado!).CancelAfter(PrazoDoCancelamento), desistir))
            {
                cortado = await _cortador.ProximoAsync(espaco => _fluxo.ReadAsync(espaco, desistir.Token)).ConfigureAwait(false);
            }

            if (cortado is { } partes)
            {
                var lote = new LoteLinhasPostgreSql(Pedido, partes.Dados, partes.Inicios, partes.Linhas, _pool);

                // O lote pode ter saído do buffer do Npgsql depois do pedido de cancelamento: o pedido vence.
                if (cancelamento.IsCancellationRequested)
                {
                    lote.Dispose();
                    cancelamento.ThrowIfCancellationRequested();
                }

                return lote;
            }

            await ConcluirAsync(cancelamento).ConfigureAwait(false);
            return null;
        }
        catch (Exception erro) when (erro is not IncognitoException)
        {
            cancelamento.ThrowIfCancellationRequested();
            throw ErrosDaLeitura.Traduzir(erro, _emReplica, naAbertura: false) ?? ErrosDaLeitura.LinhasNaoLidas(_tabela);
        }
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            if (!_concluida)
            {
                // No meio: pede ao servidor que pare, drena o que ainda vem e volta ao ponto, tudo com prazo; sem resposta,
                // desiste da conexão (o servidor desfaz a transação, e o leitor falha na próxima leitura).
                using var prazo = new CancellationTokenSource(PrazoDoCancelamento);
                if (!_cortador.FimDoFluxo)
                {
                    PedirCancelamento();
                    await DrenarAsync(prazo.Token).ConfigureAwait(false);
                }

                try
                {
                    if (_exportacao is IAsyncDisposable assincrona)
                    {
                        await assincrona.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        _exportacao.Dispose();
                    }

                    await SessaoLeituraPostgreSql.ExecutarAsync(_conexao, $"ROLLBACK TO SAVEPOINT {_ponto}; RELEASE SAVEPOINT {_ponto}", prazo.Token).ConfigureAwait(false);
                }
                catch (Exception erro) when (erro is NpgsqlException or InvalidOperationException or OperationCanceledException or ObjectDisposedException or IOException)
                {
                    await _conexao.CloseAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _cortador.Limpar();
            _aoDescartar(this);
        }
    }

    // O pedido ao servidor, fora da thread de quem cancela (o Cancel do Npgsql é síncrono e espera a rede); nunca lança.
    private void PedirCancelamento() => ThreadPool.UnsafeQueueUserWorkItem(
        static fluxo =>
        {
            try
            {
                fluxo.Cancel();
            }
            catch (Exception erro) when (erro is NpgsqlException or InvalidOperationException or ObjectDisposedException or IOException)
            {
            }
        },
        _fluxo,
        preferLocal: false);

    // O que ainda vem do COPY interrompido, até o fim ou o prazo; os bytes são da origem e somem do buffer.
    private async Task DrenarAsync(CancellationToken prazo)
    {
        var buffer = _pool.Rent(8192);
        var maior = 0;
        try
        {
            int lidos;
            while ((lidos = await _fluxo.ReadAsync(buffer, prazo).ConfigureAwait(false)) > 0)
            {
                maior = Math.Max(maior, lidos);
            }
        }
        catch (Exception erro) when (erro is NpgsqlException or InvalidOperationException or OperationCanceledException or ObjectDisposedException or IOException)
        {
            maior = buffer.Length;
        }
        finally
        {
            Buffers.Devolver(_pool, buffer, maior);
        }
    }

    // O fim do COPY: fecha a exportação, confere que a sessão seguiu intacta e solta o ponto de salvamento.
    private async Task ConcluirAsync(CancellationToken cancelamento)
    {
        _exportacao.Dispose();
        if (_antesDaConclusao is not null)
        {
            await _antesDaConclusao(_conexao, cancelamento).ConfigureAwait(false);
        }

        await using (var conferir = new NpgsqlCommand(LeitorDaSessaoPostgreSql.SessaoIntacta, _conexao))
        {
            if (await conferir.ExecuteScalarAsync(cancelamento).ConfigureAwait(false) is not true)
            {
                throw ErrosDaLeitura.TextoTrocado();
            }
        }

        // Sem o token: o cancelamento depois de o servidor soltar o ponto faria voltar a um ponto que não existe mais.
        await SessaoLeituraPostgreSql.ExecutarAsync(_conexao, $"RELEASE SAVEPOINT {_ponto}", CancellationToken.None).ConfigureAwait(false);
        _concluida = true;
    }
}
