// O comparador lê o esquema dos dois bancos, tipos do contrato experimental de dialeto que o motor declara; nada daqui é
// público.
#pragma warning disable INCX001

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using CodeProcess.Incognito.Configuracao;
using CodeProcess.Incognito.Dialetos;
using CodeProcess.Incognito.Execucao;
using CodeProcess.Incognito.Transformadores;

namespace CodeProcess.Incognito.Interno;

/// <summary>
/// A comparação da carga do <c>planejar</c> com a do replanejamento da execução, campo a campo, sobre uma forma canônica
/// ordinal (não é hash): toda propriedade pública dos tipos do modelo entra, salvo as exclusões declaradas aqui, e um tipo
/// sem forma canônica é defeito (um teste por reflexão garante que nenhum tipo alcançável fica de fora). A forma nunca sai
/// daqui: a divergência diz só qual tabela, coluna ou domínio mudou, nunca o conteúdo.
/// </summary>
internal static class ComparadorDaCarga
{
    /// <summary>
    /// O que nunca se compara, nos dois lados:
    /// <list type="bullet">
    /// <item>o acesso (as permissões mudam sem mudar o que se copia; a montagem refeita confere o acesso de novo);</item>
    /// <item>a identidade lida (a proteção confere o banco esperado, e o marcador muda com a execução);</item>
    /// <item>as chaves removidas e as triggers desligadas (saem do plano refeito, porque o reparo as muda);</item>
    /// <item>a instância da fábrica (a mesma do registro: os metadados registrados é que se comparam).</item>
    /// </list>
    /// </summary>
    internal static readonly IReadOnlySet<(Type Tipo, string Propriedade)> ExcluidasSempre = new HashSet<(Type, string)>
    {
        (typeof(EsquemaBanco), nameof(EsquemaBanco.Acesso)),
        (typeof(LeituraDoBanco), nameof(LeituraDoBanco.Identidade)),
        (typeof(DadosDeExecucao), nameof(DadosDeExecucao.ChavesRemovidas)),
        (typeof(DadosDeExecucao), nameof(DadosDeExecucao.TriggersDesligadas)),
        (typeof(FabricaRegistrada), nameof(FabricaRegistrada.Fabrica)),
    };

    /// <summary>
    /// O que não se compara no esquema do destino: as chaves estrangeiras e se cada trigger está ligada, que o reparo de uma
    /// execução interrompida muda antes do replanejamento (as listas do diário saem do plano refeito); e os objetos do esquema
    /// reservado, que o <c>preparar-destino</c> cria depois de um <c>planejar</c> sem o diário (a montagem refeita recusa o
    /// objeto que não é do diário).
    /// </summary>
    internal static readonly IReadOnlySet<(Type Tipo, string Propriedade)> ExcluidasNoDestino = new HashSet<(Type, string)>
    {
        (typeof(TabelaEsquema), nameof(TabelaEsquema.ChavesEstrangeiras)),
        (typeof(TriggerEsquema), nameof(TriggerEsquema.Ligada)),
        (typeof(EsquemaBanco), nameof(EsquemaBanco.ObjetosNoEsquemaReservado)),
    };

    /// <summary>
    /// A primeira divergência entre as duas cargas, ou nula quando são iguais: a mais precisa primeiro (uma coluna tratada, uma
    /// unidade, um domínio) e só depois o esquema e a configuração, na ordem da carga original.
    /// </summary>
    /// <param name="original">A carga do <c>planejar</c>.</param>
    /// <param name="refeita">A carga do replanejamento.</param>
    /// <returns>O que divergiu, como "a tabela x.y", "a coluna x.y.z" ou "o domínio d", com os nomes escapados.</returns>
    internal static string? Divergencia(DadosDeExecucao original, DadosDeExecucao refeita)
    {
        var antes = Escrever(original);
        var depois = Escrever(refeita);
        var textoAntes = antes.ToDictionary(entrada => entrada.Local, entrada => entrada.Texto, StringComparer.Ordinal);
        var textoDepois = depois.ToDictionary(entrada => entrada.Local, entrada => entrada.Texto, StringComparer.Ordinal);
        return antes.Concat(depois.Where(entrada => !textoAntes.ContainsKey(entrada.Local)))
            .OrderBy(entrada => entrada.Precisao)
            .Select(entrada => entrada.Local)
            .FirstOrDefault(local => !textoAntes.TryGetValue(local, out var texto) || !textoDepois.TryGetValue(local, out var outro) || !string.Equals(texto, outro, StringComparison.Ordinal));
    }

    /// <summary>
    /// A forma canônica da carga, uma entrada por lugar (a carga, cada banco, cada tabela, sequence e objeto de cada esquema,
    /// cada unidade, cada coluna tratada e cada domínio), na ordem em que aparecem. Só para comparar, nunca para mostrar.
    /// </summary>
    /// <param name="carga">A carga.</param>
    /// <returns>As entradas, por lugar.</returns>
    internal static IReadOnlyList<KeyValuePair<string, string>> EntradasEmOrdem(DadosDeExecucao carga)
    {
        ArgumentNullException.ThrowIfNull(carga);
        return [.. Escrever(carga).Select(entrada => new KeyValuePair<string, string>(entrada.Local, entrada.Texto))];
    }

    /// <summary>As propriedades que a forma canônica leu, por tipo, para o teste por reflexão.</summary>
    /// <param name="carga">A carga.</param>
    /// <returns>Cada tipo e propriedade lidos.</returns>
    internal static IReadOnlySet<(Type Tipo, string Propriedade)> Lidas(DadosDeExecucao carga)
    {
        ArgumentNullException.ThrowIfNull(carga);
        var escritor = new Escritor();
        escritor.Entrada("a carga", carga);
        return escritor.LidasPorTipo;
    }

    private static IReadOnlyList<(string Local, string Texto, int Precisao)> Escrever(DadosDeExecucao carga)
    {
        ArgumentNullException.ThrowIfNull(carga);
        var escritor = new Escritor();
        escritor.Entrada("a carga", carga);
        return escritor.Entradas;
    }

    // Escreve a forma canônica: cada lugar numa entrada própria, de que o pai não guarda nada (a falta ou a sobra de um
    // lugar já é divergência, e a ordem de tabelas, sequences e objetos no catálogo não importa).
    private sealed class Escritor
    {
        private const int Profundidade = 32;

        private readonly List<(string Local, string Texto, int Precisao)> _entradas = [];
        private readonly HashSet<string> _locais = new(StringComparer.Ordinal);
        private readonly HashSet<(Type, string)> _lidas = [];
        private string _lado = "";
        private bool _noDestino;
        private NomeTabela? _unidade;

        internal IReadOnlyList<(string Local, string Texto, int Precisao)> Entradas => _entradas;

        internal IReadOnlySet<(Type Tipo, string Propriedade)> LidasPorTipo => _lidas;

        internal void Entrada(string local, object valor)
        {
            // Dois lugares com o mesmo nome (não acontece num esquema coerente) ficam distintos pela ordem.
            var unico = local;
            for (var vez = 2; !_locais.Add(unico); vez++)
            {
                unico = string.Create(CultureInfo.InvariantCulture, $"{local} ({vez})");
            }

            var indice = _entradas.Count;
            _entradas.Add(default);
            var texto = new StringBuilder();
            Objeto(valor, texto, 0);
            _entradas[indice] = (unico, texto.ToString(), valor switch
            {
                ColunaTratada => 0,
                UnidadeDeCopia => 1,
                DefinicaoDominio => 2,
                _ => 3,
            });
        }

        private void Valor(object? valor, StringBuilder texto, int nivel)
        {
            if (nivel > Profundidade)
            {
                throw new InvalidOperationException("A carga é funda demais para a forma canônica.");
            }

            switch (valor)
            {
                case null:
                    texto.Append('~');
                    return;
                case string cadeia:
                    texto.Append('s').Append(cadeia.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(cadeia);
                    return;
                case bool logico:
                    texto.Append(logico ? 'V' : 'F');
                    return;
                case Enum enumerado:
                    texto.Append('e').Append(Convert.ToInt64(enumerado, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return;
                case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                    texto.Append('n').Append(((IFormattable)valor).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case Guid guid:
                    texto.Append('g').Append(guid.ToString("D"));
                    return;
                case DateTimeOffset instante:
                    texto.Append('t').Append(instante.ToString("O", CultureInfo.InvariantCulture));
                    return;
                case Version versao:
                    texto.Append('v').Append(versao.ToString());
                    return;
            }

            var tipo = valor.GetType();
            if (Lugar(valor) is { } local)
            {
                // O lugar vira entrada própria; o pai não guarda nada dele.
                var (unidade, lado) = (_unidade, _lado);
                if (valor is UnidadeDeCopia copia)
                {
                    _unidade = copia.Nome;
                }

                Entrada(local, valor);
                (_unidade, _lado) = (unidade, lado);
                return;
            }

            if (Pares(valor) is { } pares)
            {
                // Dicionário: pela forma da chave, em ordem ordinal.
                Lista(pares.Select(par => Escrito(par.Chave, nivel) + "=" + Escrito(par.Valor, nivel)).Order(StringComparer.Ordinal), '{', '}', texto);
                return;
            }

            if (tipo.GetInterfaces().Any(interfaceDoTipo => interfaceDoTipo.IsGenericType && interfaceDoTipo.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)))
            {
                // Conjunto: pela forma de cada item, em ordem ordinal.
                Lista(((IEnumerable)valor).Cast<object?>().Select(item => Escrito(item, nivel)).Order(StringComparer.Ordinal), '<', '>', texto);
                return;
            }

            if (valor is IEnumerable itens)
            {
                // Lista: na ordem dela, sem os lugares (que viram entradas).
                var escritos = new List<string>();
                foreach (var item in itens)
                {
                    if (item is not null && Lugar(item) is not null)
                    {
                        Valor(item, texto, nivel + 1);
                    }
                    else
                    {
                        escritos.Add(Escrito(item, nivel));
                    }
                }

                Lista(escritos, '[', ']', texto);
                return;
            }

            if (DoModelo(tipo))
            {
                Objeto(valor, texto, nivel + 1);
                return;
            }

            throw new InvalidOperationException($"A forma canônica da carga não conhece o tipo {tipo.Name}.");
        }

        // Um objeto do modelo: cada propriedade pública, em ordem ordinal do nome, fora as excluídas.
        private void Objeto(object valor, StringBuilder texto, int nivel)
        {
            var tipo = valor.GetType();
            if (!DoModelo(tipo))
            {
                throw new InvalidOperationException($"A forma canônica da carga não conhece o tipo {tipo.Name}.");
            }

            texto.Append(tipo.Name).Append('(');
            foreach (var propriedade in Propriedades(tipo))
            {
                if (ExcluidasSempre.Contains((tipo, propriedade.Name)) || (_noDestino && ExcluidasNoDestino.Contains((tipo, propriedade.Name))))
                {
                    continue;
                }

                _lidas.Add((tipo, propriedade.Name));
                var (lado, noDestino) = (_lado, _noDestino);
                if (valor is DadosDeExecucao)
                {
                    // Cada banco com o seu lado: o destino tem exclusões próprias.
                    (_lado, _noDestino) = propriedade.Name switch
                    {
                        nameof(DadosDeExecucao.Origem) => ("da origem", false),
                        nameof(DadosDeExecucao.Destino) => ("do destino", true),
                        _ => (_lado, _noDestino),
                    };
                }

                texto.Append(propriedade.Name).Append('=');
                Valor(propriedade.GetValue(valor), texto, nivel);
                texto.Append(';');
                (_lado, _noDestino) = (lado, noDestino);
            }

            texto.Append(')');
        }

        private string Escrito(object? valor, int nivel)
        {
            var texto = new StringBuilder();
            Valor(valor, texto, nivel + 1);
            return texto.ToString();
        }

        // O nome do lugar, com os nomes escapados; nulo para o que não é lugar.
        private string? Lugar(object valor) => valor switch
        {
            LeituraDoBanco => $"o banco {_lado}",
            UnidadeDeCopia unidade => $"a tabela {unidade.Nome}",
            ColunaTratada tratada => $"a coluna {new NomeColuna(_unidade ?? throw new InvalidOperationException("Coluna tratada fora de uma unidade."), tratada.Coluna)}",
            DefinicaoDominio dominio => $"o domínio {TextoSeguro.Nome(dominio.Nome)}",
            TabelaEsquema tabela => $"a tabela {Exibir(tabela.Nome)} {_lado}",
            SequenceEsquema sequence => $"a sequence {Exibir(sequence.Nome)} {_lado}",
            ObjetoResidual residual => $"o objeto {Exibir(residual.Nome)} {_lado}",
            _ => null,
        };

        private static string Exibir(NomeObjeto nome) => new NomeTabela(nome.Esquema, nome.Nome).ToString();

        private static void Lista(IEnumerable<string> itens, char abre, char fecha, StringBuilder texto)
        {
            texto.Append(abre);
            foreach (var item in itens)
            {
                texto.Append(item).Append(',');
            }

            texto.Append(fecha);
        }

        // Os pares de um dicionário genérico (todo dicionário do modelo enumera KeyValuePair).
        private static List<(object? Chave, object? Valor)>? Pares(object valor)
        {
            var dicionario = valor.GetType().GetInterfaces()
                .FirstOrDefault(interfaceDoTipo => interfaceDoTipo.IsGenericType && interfaceDoTipo.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
            if (dicionario is null)
            {
                return null;
            }

            var par = typeof(KeyValuePair<,>).MakeGenericType(dicionario.GetGenericArguments());
            var chave = par.GetProperty(nameof(KeyValuePair<int, int>.Key))!;
            var conteudo = par.GetProperty(nameof(KeyValuePair<int, int>.Value))!;
            return ((IEnumerable)valor).Cast<object>().Select(item => (chave.GetValue(item), conteudo.GetValue(item))).ToList();
        }
    }

    /// <summary>Se o tipo é do modelo: uma classe ou struct do Incognito, que não é delegado nem coleção.</summary>
    /// <param name="tipo">O tipo.</param>
    /// <returns>Se a forma canônica o percorre pelas propriedades.</returns>
    internal static bool DoModelo(Type tipo) =>
        tipo.Namespace?.StartsWith("CodeProcess.Incognito", StringComparison.Ordinal) == true
        && !typeof(Delegate).IsAssignableFrom(tipo)
        && !typeof(IEnumerable).IsAssignableFrom(tipo)
        && !tipo.IsEnum
        && (tipo.IsClass || tipo.IsValueType);

    /// <summary>As propriedades públicas de instância de um tipo do modelo, sem indexadores, em ordem ordinal do nome.</summary>
    /// <param name="tipo">O tipo.</param>
    /// <returns>As propriedades.</returns>
    internal static IEnumerable<PropertyInfo> Propriedades(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(propriedade => propriedade.GetIndexParameters().Length == 0 && propriedade.GetMethod is { IsPublic: true })
            .OrderBy(propriedade => propriedade.Name, StringComparer.Ordinal);
}
