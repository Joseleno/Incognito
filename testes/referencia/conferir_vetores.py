#!/usr/bin/env python3
"""Referência independente dos vetores fixos do CodeProcess.Incognito.

Reimplementa, só com a biblioteca padrão, a derivação de chaves, o Feistel modular com cycle-walking, os dígitos
verificadores e cada transformador com as políticas de entrada, e confere todos os arquivos de ``testes/vetores/``.
Também confere antes as âncoras de ``testes/ancoras/`` (RFC 4231, RFC 5869 e os exemplos didáticos do CNPJ
alfanumérico), que não vêm da implementação em C#.

As regras e as tabelas foram transcritas das normas citadas e da descrição de cada transformador, não do código em C#:
a referência pega erros de codificação e de ordem de bytes; um erro de interpretação repetido nas duas não aparece.

Uso (no Windows, ``py -3 -I`` no lugar de ``python3 -I``):
    python3 -I testes/referencia/conferir_vetores.py               confere âncoras e vetores
    python3 -I testes/referencia/conferir_vetores.py --autoteste   também prova que alterações são acusadas
    python3 -I testes/referencia/conferir_vetores.py --base REF    também exige que os vetores de REF continuem iguais
    python3 -I conferir_vetores.py --raiz DIR --base REF --so-crescimento
                                                                   só a guarda, sobre o repositório em DIR (o CI roda
                                                                   assim a versão do script que está na base)

As mensagens citam arquivo, caso e par, nunca valores.
"""

import argparse
import base64
import hashlib
import hmac
import json
import os
import re
import subprocess
import sys

if sys.version_info < (3, 12):
    sys.exit("A referência precisa de Python 3.12 ou mais novo.")

RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PASTA_VETORES = os.path.join(RAIZ, "testes", "vetores")
PASTA_ANCORAS = os.path.join(RAIZ, "testes", "ancoras")


class Falha(Exception):
    """Divergência ou arquivo fora do formato; a mensagem nunca traz valores."""

    def __init__(self, onde, motivo):
        super().__init__(f"{onde}: {motivo}.")


# ---------------------------------------------------------------------------------------------------------------------
# Texto como unidades UTF-16: tamanhos e posições contados em unidades (um emoji conta 2), como no texto das colunas.
# ---------------------------------------------------------------------------------------------------------------------

def u16(texto):
    dados = texto.encode("utf-16-le", "surrogatepass")
    return "".join(chr(int.from_bytes(dados[i:i + 2], "little")) for i in range(0, len(dados), 2))


def u16_de_hex(hexadecimais, onde):
    if not hexadecimais or len(hexadecimais) % 4 or re.fullmatch(r"[0-9A-F]+", hexadecimais, re.ASCII) is None:
        raise Falha(onde, "texto em UTF-16 fora do formato")
    return "".join(chr(int(hexadecimais[i:i + 4], 16)) for i in range(0, len(hexadecimais), 4))


def utf8_de_u16(texto, recusar_invalido):
    """UTF-8 do texto em unidades UTF-16. Surrogate sem par: None (recusar) ou U+FFFD por unidade."""
    dados = "".join(texto).encode("utf-16-le", "surrogatepass")
    try:
        return dados.decode("utf-16-le").encode("utf-8")
    except UnicodeDecodeError:
        if recusar_invalido:
            return None
        return dados.decode("utf-16-le", errors="replace").encode("utf-8")


DIGITOS = "0123456789"
MAIUSCULAS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
MINUSCULAS = "abcdefghijklmnopqrstuvwxyz"
HEXADECIMAIS = "0123456789abcdefABCDEF"

# Espaços tirados das pontas do e-mail e da entidade do CEP: categorias Zs, Zl e Zp do Unicode mais U+0009 a U+000D e
# U+0085.
ESPACOS = set(chr(c) for c in [*range(0x09, 0x0E), 0x20, 0x85, 0xA0, 0x1680, *range(0x2000, 0x200B), 0x2028, 0x2029, 0x202F, 0x205F, 0x3000])

# Controles recusados no e-mail: categoria Cc do Unicode (U+0000 a U+001F e U+007F a U+009F).
CONTROLES = set(chr(c) for c in [*range(0x00, 0x20), *range(0x7F, 0xA0)])


def so_digitos(texto):
    return all(c in DIGITOS for c in texto)


def tirar_espacos_a_direita(texto):
    """Só U+0020: o preenchimento de char(n)."""
    fim = len(texto)
    while fim > 0 and texto[fim - 1] == " ":
        fim -= 1
    return texto[:fim], texto[fim:]


def aparar(texto):
    inicio, fim = 0, len(texto)
    while inicio < fim and texto[inicio] in ESPACOS:
        inicio += 1
    while fim > inicio and texto[fim - 1] in ESPACOS:
        fim -= 1
    return texto[inicio:fim]


# ---------------------------------------------------------------------------------------------------------------------
# Chave e derivação: PRK = HKDF-Extract("CodeProcess.Incognito/v1", chave) (RFC 5869); subchaves por HKDF-Expand com o
# info em ASCII separado por 0x00; nomes na regra [a-z0-9]+([._-][a-z0-9]+)*, até 64 caracteres.
# ---------------------------------------------------------------------------------------------------------------------

SAL = b"CodeProcess.Incognito/v1"
NOME = re.compile(r"[a-z0-9]+([._-][a-z0-9]+)*", re.ASCII)
VARIANTES = {"num", "alfa", "cel", "fixo", "ordem", "id", "nnf", "nff-dia", "nff-seq", "cnf8", "cnf7"}


def ler_chave(texto, onde):
    """inc1_ e 32 bytes em base64url sem padding, na forma canônica; a chave zero é recusada."""
    if not isinstance(texto, str) or len(texto) != 48 or not texto.startswith("inc1_") or re.fullmatch(r"[A-Za-z0-9_-]{43}", texto[5:], re.ASCII) is None:
        raise Falha(onde, "chave fora do formato")
    valor = base64.urlsafe_b64decode(texto[5:] + "=")
    if base64.urlsafe_b64encode(valor).rstrip(b"=").decode("ascii") != texto[5:] or valor == bytes(32):
        raise Falha(onde, "chave fora da forma canônica")
    return valor


def hkdf_extrair(sal, chave):
    return hmac.digest(sal, chave, "sha256")


def hkdf_expandir(prk, info, tamanho):
    saida, bloco, contador = b"", b"", 1
    while len(saida) < tamanho:
        bloco = hmac.digest(prk, bloco + info + bytes([contador]), "sha256")
        saida += bloco
        contador += 1
    return saida[:tamanho]


def validar_nome(nome, onde):
    if not isinstance(nome, str) or len(nome) > 64 or NOME.fullmatch(nome) is None:
        raise Falha(onde, "nome fora da regra dos nomes")
    return nome


def chave_de_dominio(chave, dominio, versao, variante, ambiente):
    if variante not in VARIANTES:
        raise Falha("derivação", "variante desconhecida")
    campos = [f"{validar_nome(dominio, 'derivação')}@{versao}", variante, validar_nome(ambiente, "derivação")]
    info = b"feistel" + b"".join(b"\x00" + c.encode("ascii") for c in campos)
    return hkdf_expandir(hkdf_extrair(SAL, chave), info, 32)


def chave_de_semente(chave, transformador, dominio, versao, ambiente):
    campos = [validar_nome(transformador, "derivação"), f"{validar_nome(dominio, 'derivação')}@{versao}", validar_nome(ambiente, "derivação")]
    info = b"semente" + b"".join(b"\x00" + c.encode("ascii") for c in campos)
    return hkdf_expandir(hkdf_extrair(SAL, chave), info, 32)


def impressao_digital(chave):
    k = hkdf_expandir(hkdf_extrair(SAL, chave), b"impressao-digital", 32)
    return hmac.digest(k, b"v1", "sha256")[:8].hex()


# ---------------------------------------------------------------------------------------------------------------------
# Feistel modular sobre Z_a × Z_b no estilo do FF1 (NIST SP 800-38G), sem binarizar, com cycle-walking: função de
# rodada HMAC-SHA256 sobre "R" ‖ u8(i) ‖ u32be(radix) ‖ u64be(a) ‖ u64be(b) ‖ u16be(|ajuste|) ‖ ajuste ‖ u64be(v).
# ---------------------------------------------------------------------------------------------------------------------

# Passos de cada cycle-walking, na ordem, com o nome da permutação: os ramos de caminhada conferem que a entrada caminhou
# de verdade, e na chave de DF-e na parte do nome do ramo.
PASSOS = []


class Feistel:
    def __init__(self, chave, radix, a, b, rodadas=12, nome=""):
        if rodadas % 2 or rodadas < 10 or not 2 <= a <= 0xFFFFFFFF or not 2 <= b <= 0xFFFFFFFF or max(a, b) > radix * min(a, b):
            raise Falha("Feistel", "parâmetros fora do permitido")
        self.chave, self.radix, self.a, self.b, self.rodadas, self.nome = chave, radix, a, b, rodadas, nome

    def _rodada(self, i, v, modulo, ajuste):
        mensagem = (b"R" + bytes([i]) + self.radix.to_bytes(4, "big") + self.a.to_bytes(8, "big") + self.b.to_bytes(8, "big")
                    + len(ajuste).to_bytes(2, "big") + ajuste + v.to_bytes(8, "big"))
        return int.from_bytes(hmac.digest(self.chave, mensagem, "sha256")[:8], "big") % modulo

    def aplicar(self, x, ajuste=b""):
        if not 0 <= x < self.a * self.b:
            raise Falha("Feistel", "valor fora do domínio")
        esquerda, direita = divmod(x, self.b)
        for i in range(self.rodadas):
            modulo = self.a if i % 2 == 0 else self.b
            esquerda, direita = direita, (esquerda + self._rodada(i, direita, modulo, ajuste)) % modulo
        return esquerda * self.b + direita

    def permutar(self, x, ajuste=b"", excluido=lambda v: False, adicional=None):
        """Aplica de novo enquanto cair no excluído; entrada excluída é ponto fixo. Registra os passos."""
        if excluido(x) or x == adicional:
            PASSOS.append((self.nome, 0))
            return x
        y = x
        for passo in range(1, 65):
            y = self.aplicar(y, ajuste)
            if not excluido(y) and y != adicional:
                PASSOS.append((self.nome, passo))
                return y
        raise Falha("Feistel", "limite de passos do cycle-walking")


# ---------------------------------------------------------------------------------------------------------------------
# Dígitos verificadores (módulo 11; no CNPJ alfanumérico, o valor de cada caractere é ASCII − 48, como na
# nota técnica conjunta da Receita).
# ---------------------------------------------------------------------------------------------------------------------

def _modulo11(soma):
    resto = soma % 11
    return 0 if resto < 2 else 11 - resto


def dv_cpf(base9):
    d = [int(c) for c in base9]
    primeiro = _modulo11(sum(v * p for v, p in zip(d, range(10, 1, -1))))
    segundo = _modulo11(sum(v * p for v, p in zip(d + [primeiro], range(11, 1, -1))))
    return primeiro * 10 + segundo


def dv_cnpj(base12):
    valores = [ord(c) - 48 for c in base12]
    pesos = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
    primeiro = _modulo11(sum(v * p for v, p in zip(valores, pesos[1:])))
    segundo = _modulo11(sum(v * p for v, p in zip(valores + [primeiro], pesos)))
    return primeiro * 10 + segundo


def cnpj_valido(cnpj):
    """CNPJ de 14 caracteres ou com a máscara 00.000.000/0000-00, raiz e ordem em maiúsculas, DV certo."""
    if len(cnpj) == 18 and cnpj[2] == "." and cnpj[6] == "." and cnpj[10] == "/" and cnpj[15] == "-":
        cnpj = cnpj[0:2] + cnpj[3:6] + cnpj[7:10] + cnpj[11:15] + cnpj[16:18]
    return (len(cnpj) == 14 and all(c in DIGITOS + MAIUSCULAS for c in cnpj[:12]) and so_digitos(cnpj[12:])
            and dv_cnpj(cnpj[:12]) == int(cnpj[12:]))


# ---------------------------------------------------------------------------------------------------------------------
# Políticas de entrada (entradaInvalida e entradaMalformada: preservar, anular, falhar) e o resultado de um par.
# ---------------------------------------------------------------------------------------------------------------------

class Erro(Exception):
    def __init__(self, codigo):
        super().__init__(codigo)
        self.codigo = codigo


def malformado(politica, entrada):
    if politica == "falhar":
        raise Erro("INC1003")
    if politica == "anular":
        return None, "AnuladoMalformado"
    return entrada, "CopiadoEmClaro"


def seguir_com_dv_invalido(politica):
    if politica == "falhar":
        raise Erro("INC1004")
    return politica == "preservar"


# ---------------------------------------------------------------------------------------------------------------------
# br.cpf: base de 9 dígitos em 10⁴ × 10⁵, repdígitos excluídos e em ponto fixo.
# ---------------------------------------------------------------------------------------------------------------------

REPDIGITOS_CPF = {d * 111_111_111 for d in range(10)}


class Cpf:
    def __init__(self, chave, dominio, versao, ambiente, invalida, malformada):
        self.feistel = Feistel(chave_de_dominio(chave, dominio, versao, "num", ambiente), 10, 10**4, 10**5, nome="cpf")
        self.invalida, self.malformada = invalida, malformada

    def _transformar(self, base, dv):
        deslocamento = (dv - dv_cpf(f"{base:09d}")) % 100
        if deslocamento and not seguir_com_dv_invalido(self.invalida):
            return None, "AnuladoDvInvalido"
        if base in REPDIGITOS_CPF:
            return (base, dv), "PontoFixo"
        nova = self.feistel.permutar(base, excluido=lambda v: v in REPDIGITOS_CPF)
        return (nova, (dv_cpf(f"{nova:09d}") + deslocamento) % 100), "DvInvalidoPreservado" if deslocamento else "Pseudonimizado"

    def texto(self, entrada):
        corpo, preenchimento = tirar_espacos_a_direita(entrada)
        if not corpo:
            return entrada, "Vazio"
        if len(corpo) == 14 and corpo[3] == "." and corpo[7] == "." and corpo[11] == "-":
            digitos, mascara = corpo[0:3] + corpo[4:7] + corpo[8:11] + corpo[12:14], True
        elif 9 <= len(corpo) <= 11:
            zeros = 11 - len(corpo)
            digitos, mascara = "0" * zeros + corpo, False
            preenchimento = preenchimento[min(zeros, len(preenchimento)):]
        else:
            return malformado(self.malformada, entrada)
        if not so_digitos(digitos):
            return malformado(self.malformada, entrada)
        partes, desfecho = self._transformar(int(digitos[:9]), int(digitos[9:]))
        if partes is None:
            return None, desfecho
        texto = f"{partes[0]:09d}{partes[1]:02d}"
        if mascara:
            texto = f"{texto[:3]}.{texto[3:6]}.{texto[6:9]}-{texto[9:]}"
        return texto + preenchimento, desfecho

    def numero(self, entrada):
        if not 0 <= entrada <= 99_999_999_999:
            return malformado(self.malformada, entrada)
        partes, desfecho = self._transformar(entrada // 100, entrada % 100)
        return (None if partes is None else partes[0] * 100 + partes[1]), desfecho

    def na_chave(self, cpf):
        """Emitente da chave de DF-e: sem a política de DV da coluna (δ mod 100, contado). Devolve (cpf, ocorrências)."""
        base, dv = int(cpf[:9]), int(cpf[9:])
        deslocamento = (dv - dv_cpf(cpf[:9])) % 100
        ocorrencias = {"DvDoEmitenteInvalido"} if deslocamento else set()
        if base in REPDIGITOS_CPF:
            return cpf, ocorrencias | {"EmitentePontoFixo"}
        nova = self.feistel.permutar(base, excluido=lambda v: v in REPDIGITOS_CPF)
        return f"{nova:09d}{(dv_cpf(f'{nova:09d}') + deslocamento) % 100:02d}", ocorrencias


# ---------------------------------------------------------------------------------------------------------------------
# br.cnpj: raiz (ou raiz e ordem) particionada por num/alfa pela presença de letra; base 36 com 0-9
# e A-Z valendo 0 a 35 (nunca ASCII − 48, que colide); raiz zero (com qualquer ordem) em ponto fixo.
# ---------------------------------------------------------------------------------------------------------------------

BASE36 = DIGITOS + MAIUSCULAS


class Particionada:
    def __init__(self, chave_numerica, chave_alfanumerica, tamanho, zeros_fixos, zeros_excluidos=0):
        maior, menor = (tamanho + 1) // 2, tamanho // 2
        self.tamanho = tamanho
        self.limite_numerico = 10 ** (tamanho - zeros_fixos)
        self.limite_alfanumerico = 36 ** (tamanho - zeros_fixos)
        # Regra dos zeros da chave de DF-e: as cadeias numéricas que começam com esses zeros não entram nem saem.
        self.limite_numerico_excluido = 10 ** (tamanho - zeros_excluidos) if zeros_excluidos else None
        self.numerica = Feistel(chave_numerica, 10, 10**maior, 10**menor, nome="cnpj-numerica")
        self.alfanumerica = Feistel(chave_alfanumerica, 36, 36**maior, 36**menor)

    def _so_digitos_base36(self, valor):
        for _ in range(self.tamanho):
            if valor % 36 >= 10:
                return False
            valor //= 36
        return True

    def permutar(self, cadeia, excluir_zeros=False):
        """Devolve (cadeia, fixa)."""
        if any(c in MAIUSCULAS for c in cadeia):
            valor = 0
            for c in cadeia:
                valor = valor * 36 + BASE36.index(c)
            if valor < self.limite_alfanumerico:
                return cadeia, True
            novo = self.alfanumerica.permutar(valor, excluido=lambda v: v < self.limite_alfanumerico or self._so_digitos_base36(v))
            saida = ""
            for _ in range(self.tamanho):
                novo, simbolo = divmod(novo, 36)
                saida = BASE36[simbolo] + saida
            return saida, False
        valor = int(cadeia)
        limite = self.limite_numerico
        if excluir_zeros:
            if self.limite_numerico_excluido is None or valor < self.limite_numerico_excluido:
                raise Falha("CNPJ", "cadeia que a regra dos zeros lê como CPF")
            limite = self.limite_numerico_excluido
        if valor < limite:
            return cadeia, True
        novo = self.numerica.permutar(valor, excluido=lambda v: v < limite)
        return f"{novo:0{self.tamanho}d}", False


class Cnpj:
    def __init__(self, chave, dominio, versao, ambiente, permutar_ordem, invalida, malformada):
        # Os 3 zeros do CPF na chave de DF-e: na regra dos zeros, essas cadeias numéricas são lidas como CPF.
        if permutar_ordem:
            k = chave_de_dominio(chave, dominio, versao, "ordem", ambiente)
            self.particionada, self.permutados = Particionada(k, k, 12, 8, 3), 12
        else:
            self.particionada = Particionada(
                chave_de_dominio(chave, dominio, versao, "num", ambiente), chave_de_dominio(chave, dominio, versao, "alfa", ambiente), 8, 8, 3)
            self.permutados = 8
        self.invalida, self.malformada = invalida, malformada

    def _transformar(self, cnpj):
        deslocamento = (int(cnpj[12:]) - dv_cnpj(cnpj[:12])) % 100
        if deslocamento and not seguir_com_dv_invalido(self.invalida):
            return None, "AnuladoDvInvalido"
        trocados, fixa = self.particionada.permutar(cnpj[:self.permutados])
        if fixa:
            return cnpj, "PontoFixo"
        base = trocados + cnpj[self.permutados:12]
        return f"{base}{(dv_cnpj(base) + deslocamento) % 100:02d}", "DvInvalidoPreservado" if deslocamento else "Pseudonimizado"

    def texto(self, entrada):
        corpo, preenchimento = tirar_espacos_a_direita(entrada)
        if not corpo:
            return entrada, "Vazio"
        if len(corpo) == 18 and corpo[2] == "." and corpo[6] == "." and corpo[10] == "/" and corpo[15] == "-":
            caracteres, mascara = corpo[0:2] + corpo[3:6] + corpo[7:10] + corpo[11:15] + corpo[16:18], True
        elif len(corpo) == 14:
            caracteres, mascara = corpo, False
        elif len(corpo) in (12, 13) and so_digitos(corpo):
            zeros = 14 - len(corpo)
            caracteres, mascara = "0" * zeros + corpo, False
            preenchimento = preenchimento[min(zeros, len(preenchimento)):]
        else:
            return malformado(self.malformada, entrada)
        if not all(c in BASE36 for c in caracteres[:12]) or not so_digitos(caracteres[12:]):
            return malformado(self.malformada, entrada)
        saida, desfecho = self._transformar(caracteres)
        if saida is None:
            return None, desfecho
        if mascara:
            saida = f"{saida[:2]}.{saida[2:5]}.{saida[5:8]}/{saida[8:12]}-{saida[12:]}"
        return saida + preenchimento, desfecho

    def numero(self, entrada):
        if not 0 <= entrada <= 99_999_999_999_999:
            return malformado(self.malformada, entrada)
        saida, desfecho = self._transformar(f"{entrada:014d}")
        return (None if saida is None else int(saida)), desfecho

    def na_chave(self, cnpj, excluir_zeros):
        """Emitente da chave de DF-e: sem a política de DV da coluna (δ mod 100, contado). Devolve (cnpj, ocorrências)."""
        deslocamento = (int(cnpj[12:]) - dv_cnpj(cnpj[:12])) % 100
        ocorrencias = {"DvDoEmitenteInvalido"} if deslocamento else set()
        trocados, fixa = self.particionada.permutar(cnpj[:self.permutados], excluir_zeros)
        if fixa:
            return cnpj, ocorrencias | {"EmitentePontoFixo"}
        base = trocados + cnpj[self.permutados:12]
        return f"{base}{(dv_cnpj(base) + deslocamento) % 100:02d}", ocorrencias


# ---------------------------------------------------------------------------------------------------------------------
# br.telefone: DDD e classe ficam; celular 9 + 8 dígitos em 10⁴ × 10⁴ (cel), celular antigo de 8
# dígitos (6 a 9) em 4000 × 10⁴ com a mesma chave, fixo (2 a 5) em 4000 × 10⁴ (fixo) sem os prefixos 3003 e 4004;
# ajuste = DDD em ASCII; serviços (0800, 0300, 0303, 0500, 0900 com 7 ou 6 dígitos; 3003, 4004) preservados; valor
# de preenchimento (só zeros, ou o número do assinante zerado depois do 55, do 0 de tronco e do DDD) volta como veio.
# ---------------------------------------------------------------------------------------------------------------------

NAO_GEOGRAFICOS = {"800", "300", "303", "500", "900"}
NAO_GEOGRAFICOS_ANTIGOS_EM_NUMERO = {"800", "300", "303", "500"}
SEPARADORES = " ()-."


def numero_zerado(d):
    """Só zeros, ou o assinante (8 ou 9 dígitos) zerado depois do DDD, do 0 de tronco e do DDD, ou do 55 e do DDD."""
    zeros = lambda s: set(s) == {"0"}
    if zeros(d):
        return True
    n = len(d)
    return ((n in (10, 11) and zeros(d[2:]))
            or (n in (11, 12) and d[0] == "0" and zeros(d[3:]))
            or (n in (12, 13) and d[:2] == "55" and zeros(d[4:])))


def telefone_zerado(corpo):
    if len(corpo) > 32 or any(c not in DIGITOS and c not in SEPARADORES + "+" for c in corpo):
        return False
    digitos = "".join(c for c in corpo if c in DIGITOS)
    return 1 <= len(digitos) <= 13 and numero_zerado(digitos)


def servico_fixo(valor):
    return 10_030_000 <= valor < 10_040_000 or 20_040_000 <= valor < 20_050_000


class Telefone:
    def __init__(self, chave, dominio, versao, ambiente, malformada):
        de_celular = chave_de_dominio(chave, dominio, versao, "cel", ambiente)
        self.celular = Feistel(de_celular, 10, 10**4, 10**4)
        self.celular_antigo = Feistel(de_celular, 10, 4000, 10**4)
        self.fixo = Feistel(chave_de_dominio(chave, dominio, versao, "fixo", ambiente), 10, 4000, 10**4)
        self.malformada = malformada

    @staticmethod
    def _classificar(d):
        """Devolve (classe, prefixo, com_ddd) ou None."""
        n = len(d)
        prefixo = 2 if n in (12, 13) and d[:2] == "55" else 1 if n in (11, 12) and d[:1] == "0" else 0
        sem_pais = d[2:] if prefixo == 2 else d
        if len(sem_pais) in (10, 11) and sem_pais[0] == "0" and sem_pais[1:4] in NAO_GEOGRAFICOS:
            return "servico-nao-geografico", n - len(sem_pais), False
        resto = n - prefixo
        com_ddd = False
        if resto in (10, 11):
            if not ("1" <= d[prefixo] <= "9" and "1" <= d[prefixo + 1] <= "9"):
                return None
            com_ddd = True
        elif resto not in (8, 9):
            return None
        numero = d[prefixo + (2 if com_ddd else 0):]
        if len(numero) == 9:
            return ("celular", prefixo, com_ddd) if numero[0] == "9" else None
        if "2" <= numero[0] <= "5":
            valor = (int(numero[0]) - 2) * 10**7 + int(numero[1:])
            return ("servico-fixo" if servico_fixo(valor) else "fixo"), prefixo, com_ddd
        if "6" <= numero[0] <= "9":
            return "celular-antigo", prefixo, com_ddd
        return None

    @staticmethod
    def _forma_de_telefone(corpo, posicoes, prefixo, com_ddd):
        q = len(posicoes)
        inicio_do_numero = prefixo + (2 if com_ddd else 0)
        tamanho_do_numero = q - inicio_do_numero
        if any(c not in "+(" for c in corpo[:posicoes[0]]):
            return False
        if posicoes[-1] != len(corpo) - 1 or (corpo[0] == "+" and prefixo != 2):
            return False
        abre, fecha = corpo.find("("), corpo.find(")")
        if abre >= 0 or fecha >= 0:
            inicio_do_ddd = 0 if prefixo == 1 else prefixo
            if (not com_ddd or abre < 0 or fecha < 0 or corpo.rfind("(") != abre or corpo.rfind(")") != fecha
                    or abre != posicoes[inicio_do_ddd] - 1 or fecha != posicoes[inicio_do_numero - 1] + 1):
                return False
        separado_antes_dos_ultimos_4 = posicoes[q - 4] != posicoes[q - 5] + 1
        for i in range(1, q):
            separador = corpo[posicoes[i - 1] + 1:posicoes[i]]
            if not separador or (i == 2 and prefixo == 2) or (i == inicio_do_numero and com_ddd):
                continue
            antes_dos_ultimos_4 = i == q - 4
            depois_do_primeiro = tamanho_do_numero == 9 and i == inicio_do_numero + 1 and separado_antes_dos_ultimos_4
            if len(separador) != 1 or not (antes_dos_ultimos_4 or depois_do_primeiro):
                return False
        return True

    def _permutar(self, d, classe, prefixo, com_ddd):
        ajuste = d[prefixo:prefixo + 2].encode("ascii") if com_ddd else b""
        inicio = prefixo + (2 if com_ddd else 0)
        numero = d[inicio:]
        if classe == "celular":
            novo = "9" + f"{self.celular.permutar(int(numero[1:]), ajuste):08d}"
        elif classe == "celular-antigo":
            v = self.celular_antigo.permutar((int(numero[0]) - 6) * 10**7 + int(numero[1:]), ajuste)
            novo = str(6 + v // 10**7) + f"{v % 10**7:07d}"
        else:
            v = self.fixo.permutar((int(numero[0]) - 2) * 10**7 + int(numero[1:]), ajuste, excluido=servico_fixo)
            novo = str(2 + v // 10**7) + f"{v % 10**7:07d}"
        return d[:inicio] + novo

    def texto(self, entrada):
        corpo, preenchimento = tirar_espacos_a_direita(entrada)
        if not corpo:
            return entrada, "Vazio"
        if telefone_zerado(corpo):
            return entrada, "Zerado"
        digitos, posicoes = "", []
        if len(corpo) > 32:
            return malformado(self.malformada, entrada)
        for i, c in enumerate(corpo):
            if c in DIGITOS:
                if len(digitos) == 13:
                    return malformado(self.malformada, entrada)
                digitos += c
                posicoes.append(i)
            elif c not in SEPARADORES and not (c == "+" and i == 0):
                return malformado(self.malformada, entrada)
        classificacao = self._classificar(digitos)
        if classificacao is None:
            return malformado(self.malformada, entrada)
        classe, prefixo, com_ddd = classificacao
        if classe == "servico-nao-geografico":
            valida = corpo[0] != "+" or prefixo == 2
        else:
            valida = self._forma_de_telefone(corpo, posicoes, prefixo, com_ddd)
        if not valida:
            return malformado(self.malformada, entrada)
        if classe.startswith("servico"):
            return entrada, "ServicoPreservado"
        novos = self._permutar(digitos, classe, prefixo, com_ddd)
        saida = list(corpo)
        for posicao, digito in zip(posicoes, novos):
            saida[posicao] = digito
        return "".join(saida) + preenchimento, "Pseudonimizado"

    def numero(self, entrada):
        if entrada == 0:
            return entrada, "Zerado"
        if entrada < 0 or len(str(entrada)) > 13:
            return malformado(self.malformada, entrada)
        digitos = str(entrada)
        if numero_zerado(digitos):
            return entrada, "Zerado"
        if ((len(digitos) == 10 and digitos[:3] in NAO_GEOGRAFICOS)
                or (len(digitos) == 9 and digitos[:3] in NAO_GEOGRAFICOS_ANTIGOS_EM_NUMERO)):
            return entrada, "ServicoPreservado"
        classificacao = self._classificar(digitos)
        if classificacao is None:
            return malformado(self.malformada, entrada)
        classe, prefixo, com_ddd = classificacao
        if classe.startswith("servico"):
            return entrada, "ServicoPreservado"
        return int(self._permutar(digitos, classe, prefixo, com_ddd)), "Pseudonimizado"


# ---------------------------------------------------------------------------------------------------------------------
# email: 100 bits do HMAC da K_semente sobre o endereço aparado e com a regra congelada de
# minúsculas (A-Z e À-Þ menos × somam 32), base32 minúsculo da RFC 4648, domínio example.com, sempre 32 caracteres.
# ---------------------------------------------------------------------------------------------------------------------

class Email:
    def __init__(self, chave, dominio, versao, ambiente, malformada):
        self.chave = chave_de_semente(chave, "email", dominio, versao, ambiente)
        self.malformada = malformada

    @staticmethod
    def _forma(endereco):
        arroba = endereco.find("@")
        if len(endereco) > 254 or not 1 <= arroba <= 64 or endereco.rfind("@") != arroba:
            return False
        if any(c in ESPACOS or c in CONTROLES for c in endereco):
            return False
        dominio = endereco[arroba + 1:]
        return "." in dominio and dominio[0] != "." and dominio[-1] != "." and ".." not in dominio

    @staticmethod
    def _minusculas(texto):
        return "".join(chr(ord(c) + 32) if "A" <= c <= "Z" or ("\u00C0" <= c <= "\u00DE" and c != "\u00D7") else c for c in texto)

    def texto(self, entrada):
        sem_preenchimento, preenchimento = tirar_espacos_a_direita(entrada)
        endereco = aparar(sem_preenchimento)
        if not endereco:
            return entrada, "Vazio"
        if not self._forma(endereco):
            return malformado(self.malformada, entrada)
        mensagem = utf8_de_u16(self._minusculas(endereco), recusar_invalido=True)
        if mensagem is None:
            return malformado(self.malformada, entrada)
        local = base64.b32encode(hmac.digest(self.chave, mensagem, "sha256")[:13]).decode("ascii").lower()[:20]
        pseudonimo = local + "@example.com"
        excesso = max(0, len(pseudonimo) - len(sem_preenchimento))
        return pseudonimo + preenchimento[min(excesso, len(preenchimento)):], "Pseudonimizado"


# ---------------------------------------------------------------------------------------------------------------------
# texto.substituir: o texto inteiro trocado pelo texto fixo em ASCII, cortado no comprimento máximo da coluna (nulo: sem
# limite). Vazio ou só com espaços (U+0020, o preenchimento de char(n)) volta como veio. Não usa a chave nem a entrada.
# ---------------------------------------------------------------------------------------------------------------------

TEXTO_REMOVIDO = "Texto removido pelo Incognito."


class TextoSubstituir:
    def __init__(self, comprimento_maximo):
        self.saida = TEXTO_REMOVIDO if comprimento_maximo is None else TEXTO_REMOVIDO[:comprimento_maximo]

    def texto(self, entrada):
        if not tirar_espacos_a_direita(entrada)[0]:
            return entrada, "Vazio"
        return self.saida, "Substituido"


# ---------------------------------------------------------------------------------------------------------------------
# br.cep: UF pela faixa do CEP de entrada; CEP de saída nas faixas da UF, contadas em
# ordem crescente, pelo HMAC da K_semente sobre UF ‖ 0x00 ‖ entidade aparada, 8 bytes big-endian módulo o total.
# Faixas por UF dos Correios; a 78900-000 a 78999-999 (RO em tabelas antigas) só vale como entrada. De 1 a 8 zeros,
# 00000-000, 00.000-000 e o número 0 são valor de preenchimento e voltam como vieram.
# ---------------------------------------------------------------------------------------------------------------------

FAIXAS_DE_CEP = {
    "SP": [(1_000_000, 19_999_999)],
    "RJ": [(20_000_000, 28_999_999)],
    "ES": [(29_000_000, 29_999_999)],
    "MG": [(30_000_000, 39_999_999)],
    "BA": [(40_000_000, 48_999_999)],
    "SE": [(49_000_000, 49_999_999)],
    "PE": [(50_000_000, 56_999_999)],
    "AL": [(57_000_000, 57_999_999)],
    "PB": [(58_000_000, 58_999_999)],
    "RN": [(59_000_000, 59_999_999)],
    "CE": [(60_000_000, 63_999_999)],
    "PI": [(64_000_000, 64_999_999)],
    "MA": [(65_000_000, 65_999_999)],
    "PA": [(66_000_000, 68_899_999)],
    "AP": [(68_900_000, 68_999_999)],
    "AM": [(69_000_000, 69_299_999), (69_400_000, 69_899_999)],
    "RR": [(69_300_000, 69_399_999)],
    "AC": [(69_900_000, 69_999_999)],
    "DF": [(70_000_000, 72_799_999), (73_000_000, 73_699_999)],
    "GO": [(72_800_000, 72_999_999), (73_700_000, 76_799_999)],
    "RO": [(76_800_000, 76_999_999)],
    "TO": [(77_000_000, 77_999_999)],
    "MT": [(78_000_000, 78_899_999)],
    "MS": [(79_000_000, 79_999_999)],
    "PR": [(80_000_000, 87_999_999)],
    "SC": [(88_000_000, 89_999_999)],
    "RS": [(90_000_000, 99_999_999)],
}
FAIXAS_SO_DE_ENTRADA = {"RO": [(78_900_000, 78_999_999)]}


def cep_zerado(corpo):
    so_zeros = lambda s: s != "" and set(s) == {"0"}
    if 1 <= len(corpo) <= 8:
        return so_zeros(corpo)
    if len(corpo) == 9:
        return corpo[5] == "-" and so_zeros(corpo[:5] + corpo[6:])
    if len(corpo) == 10:
        return corpo[2] == "." and corpo[6] == "-" and so_zeros(corpo[:2] + corpo[3:6] + corpo[7:])
    return False


def uf_do_cep(cep):
    for tabela in (FAIXAS_DE_CEP, FAIXAS_SO_DE_ENTRADA):
        for uf, faixas in tabela.items():
            if any(inicio <= cep <= fim for inicio, fim in faixas):
                return uf
    return None


class Cep:
    def __init__(self, chave, dominio, versao, ambiente, malformada):
        self.chave = chave_de_semente(chave, "br.cep", dominio, versao, ambiente)
        self.malformada = malformada

    def _escolher(self, cep, entidade):
        uf = uf_do_cep(cep)
        if uf is None:
            return None
        mensagem = uf.encode("ascii") + b"\x00" + utf8_de_u16(aparar(entidade), recusar_invalido=False)
        faixas = sorted(FAIXAS_DE_CEP[uf])
        posicao = int.from_bytes(hmac.digest(self.chave, mensagem, "sha256")[:8], "big") % sum(fim - inicio + 1 for inicio, fim in faixas)
        for inicio, fim in faixas:
            if posicao <= fim - inicio:
                return inicio + posicao
            posicao -= fim - inicio + 1
        raise Falha("CEP", "posição fora das faixas")

    def texto(self, entrada, entidade):
        corpo, preenchimento = tirar_espacos_a_direita(entrada)
        if not corpo:
            return entrada, "Vazio"
        if cep_zerado(corpo):
            return entrada, "Zerado"
        if len(corpo) == 9 and corpo[5] == "-":
            digitos, mascara = corpo[:5] + corpo[6:], "hifen"
        elif len(corpo) == 10 and corpo[2] == "." and corpo[6] == "-":
            digitos, mascara = corpo[:2] + corpo[3:6] + corpo[7:], "ponto"
        elif len(corpo) in (7, 8):
            zeros = 8 - len(corpo)
            digitos, mascara = "0" * zeros + corpo, None
            preenchimento = preenchimento[min(zeros, len(preenchimento)):]
        else:
            return malformado(self.malformada, entrada)
        cep = self._escolher(int(digitos), entidade) if so_digitos(digitos) else None
        if cep is None:
            return malformado(self.malformada, entrada)
        texto = f"{cep:08d}"
        if mascara == "hifen":
            texto = f"{texto[:5]}-{texto[5:]}"
        elif mascara == "ponto":
            texto = f"{texto[:2]}.{texto[2:5]}-{texto[5:]}"
        return texto + preenchimento, "Pseudonimizado"

    def numero(self, entrada, entidade):
        if entrada == 0:
            return entrada, "Zerado"
        cep = self._escolher(entrada, entidade) if 0 <= entrada <= 99_999_999 else None
        if cep is None:
            return malformado(self.malformada, entrada)
        return cep, "Pseudonimizado"


# ---------------------------------------------------------------------------------------------------------------------
# id.permutar: [1, N) com N = 10^d, a = 10^⌈d/2⌉, b = 10^⌊d/2⌋, 0 excluído, ajuste = N em
# decimal ASCII; zero e negativos em ponto fixo; a partir de N, entradaMalformada.
# ---------------------------------------------------------------------------------------------------------------------

class Identificador:
    def __init__(self, chave, dominio, versao, ambiente, n, malformada):
        d = len(str(n)) - 1
        if n != 10**d or not 4 <= d <= 18:
            raise Falha("id.permutar", "N fora das potências de 10 de 10⁴ a 10¹⁸")
        self.n = n
        self.feistel = Feistel(chave_de_dominio(chave, dominio, versao, "id", ambiente), 10, 10 ** ((d + 1) // 2), 10 ** (d // 2))
        self.malformada = malformada

    def numero(self, entrada):
        if entrada <= 0:
            return entrada, "PontoFixo"
        if entrada >= self.n:
            return malformado(self.malformada, entrada)
        return self.feistel.permutar(entrada, str(self.n).encode("ascii"), excluido=lambda v: v == 0), "Pseudonimizado"


# ---------------------------------------------------------------------------------------------------------------------
# br.chave-pix: o tipo sai da forma, sem os espaços à direita; CPF, CNPJ, e-mail e
# telefone vão às instâncias das colunas; a aleatória sai do HMAC da K_semente sobre "evp" ‖ 0x00 ‖ os 16 bytes do UUID
# na ordem do texto, com a versão 4 e a variante da RFC 9562, em minúsculas.
# ---------------------------------------------------------------------------------------------------------------------

class ChavePix:
    def __init__(self, chave, dominio, versao, ambiente, malformada, componentes):
        c = componentes
        self.cpf = Cpf(chave, c["cpf"]["dominio"], c["cpf"]["versao"], ambiente, c["cpf"]["entradaInvalida"], malformada)
        self.cnpj = Cnpj(chave, c["cnpj"]["dominio"], c["cnpj"]["versao"], ambiente, c["cnpj"]["permutarOrdem"], c["cnpj"]["entradaInvalida"], malformada)
        self.email = Email(chave, c["email"]["dominio"], c["email"]["versao"], ambiente, malformada)
        self.telefone = Telefone(chave, c["telefone"]["dominio"], c["telefone"]["versao"], ambiente, malformada)
        self.chave = chave_de_semente(chave, "br.chave-pix", dominio, versao, ambiente)
        self.malformada = malformada

    @staticmethod
    def _uuid(corpo):
        return len(corpo) == 36 and all((c == "-") if i in (8, 13, 18, 23) else (c in HEXADECIMAIS) for i, c in enumerate(corpo))

    @staticmethod
    def _cpf_com_mascara(corpo):
        return (len(corpo) == 14 and corpo[3] == "." and corpo[7] == "." and corpo[11] == "-"
                and so_digitos(corpo[0:3] + corpo[4:7] + corpo[8:11] + corpo[12:14]))

    @staticmethod
    def _base_do_cnpj(corpo):
        """Os 12 primeiros (raiz e ordem) de um CNPJ com letras de qualquer caixa, com ou sem máscara; ou None."""
        alfanumericos = DIGITOS + MAIUSCULAS + MINUSCULAS
        if len(corpo) == 14 and all(c in alfanumericos for c in corpo[:12]) and so_digitos(corpo[12:]):
            return corpo[:12]
        if len(corpo) == 18 and corpo[2] == "." and corpo[6] == "." and corpo[10] == "/" and corpo[15] == "-":
            base = corpo[0:2] + corpo[3:6] + corpo[7:10] + corpo[11:15]
            if all(c in alfanumericos for c in base) and so_digitos(corpo[16:18]):
                return base
        return None

    @staticmethod
    def _telefone(corpo):
        if corpo[0] == "+":
            return len(corpo) > 1 and corpo[1] in DIGITOS
        return (corpo[0] in DIGITOS or corpo[0] == "(") and all(c in DIGITOS or c in SEPARADORES for c in corpo)

    def texto(self, entrada):
        corpo, preenchimento = tirar_espacos_a_direita(entrada)
        if not corpo:
            return entrada, "Vazio"
        if "@" in corpo:
            return self.email.texto(entrada)
        if self._uuid(corpo):
            dados = bytes.fromhex(corpo.replace("-", ""))
            b = bytearray(hmac.digest(self.chave, b"evp\x00" + dados, "sha256")[:16])
            b[6] = (b[6] & 0x0F) | 0x40
            b[8] = (b[8] & 0x3F) | 0x80
            h = b.hex()
            return f"{h[:8]}-{h[8:12]}-{h[12:16]}-{h[16:20]}-{h[20:]}" + preenchimento, "Pseudonimizado"
        if (len(corpo) == 11 and so_digitos(corpo)) or self._cpf_com_mascara(corpo):
            return self.cpf.texto(entrada)
        base = self._base_do_cnpj(corpo)
        if base is not None:
            if not any(c in MINUSCULAS for c in corpo):
                return self.cnpj.texto(entrada)
            if dv_cnpj(base.upper()) != int(corpo[-2:]):
                return malformado(self.malformada, entrada)
            saida, desfecho = self.cnpj.texto(entrada.translate(str.maketrans(MINUSCULAS, MAIUSCULAS)))
            return (entrada if desfecho == "PontoFixo" else saida), desfecho
        if self._telefone(corpo):
            return self.telefone.texto(entrada)
        return malformado(self.malformada, entrada)


# ---------------------------------------------------------------------------------------------------------------------
# br.chave-dfe: chave de acesso de 44 posições (contadas a partir de 1): cUF (1–2), AAMM (3–6), emitente (7–20: CNPJ, ou
# CPF com 000 à esquerda), modelo (21–22), série (23–25), número (26–34), tpEmis (35), código numérico (36–43; na NF3e e
# na NFCom, nSiteAutoriz em 36 e código de 7 dígitos em 37–43) e cDV (44). Estrutura: 6 dígitos, 12 dígitos ou letras
# maiúsculas, 26 dígitos. cDV: módulo 11 sobre os 43 primeiros, valor ASCII − 48, pesos 2 a 9 cíclicos da direita para
# a esquerda, resto 0 ou 1 dá 0 (código do Anexo II da NT Conjunta 2025.001, que usa 43 caracteres).
# ---------------------------------------------------------------------------------------------------------------------

def dv_chave_dfe(primeiros43):
    soma, peso = 0, 2
    for c in reversed(primeiros43):
        soma += (ord(c) - 48) * peso
        peso = 2 if peso == 9 else peso + 1
    return _modulo11(soma)


# NFF da NF-e e da NFC-e (NT 2021.002, item 2.1): número = dia (26–27), mês (28–29), tipo do emissor (30) e sequencial
# diário (31–34). NFF do CT-e e do MDF-e (NT CT-e 2020.001 v1.05, NT MDF-e 2020.002 v1.05, MOC CT-e 4.00 e MDF-e 3.00b):
# mês (26–27), dia (28–29) e sequencial (30–34), pela ordem do texto. Índices a partir de 0 na chave.
NFF_NFE = {"dia": 25, "mes": 27, "sequencial": 30, "tamanho": 4}
NFF_TRANSPORTE = {"dia": 27, "mes": 25, "sequencial": 29, "tamanho": 5}

# Por modelo: regra do emitente, tamanho do código numérico, leiaute da NFF e prefixo do Id. 55 NF-e e 65 NFC-e pelas
# séries (com o PAA só na NF-e, NT 2026.001 v1.02b, campo B07; a NFC-e não usa PAA, regra B07-10); 57 CT-e e 58 MDF-e
# pela regra dos zeros (nenhuma regra de série separa CPF de CNPJ: o PAA usa tpEmis 3 também com CNPJ de MEI, NT CT-e
# 2024.002 v1.05 e NT MDF-e 2024.002 v1.01); 67 CT-e OS, 64 GTV-e, 63 BP-e, 66 NF3e e 62 NFCom só admitem CNPJ no
# emitente (XSD de cada leiaute). Prefixos do Id: a GTV-e e o CT-e OS usam CTe (rejeição 253 da NT CT-e 2020.002).
MODELOS_DFE = {
    55: ("series-com-paa", 8, NFF_NFE, "NFe"),
    65: ("series", 8, NFF_NFE, "NFe"),
    57: ("zeros", 8, NFF_TRANSPORTE, "CTe"),
    58: ("zeros", 8, NFF_TRANSPORTE, "MDFe"),
    67: ("cnpj", 8, None, "CTe"),
    64: ("cnpj", 8, None, "CTe"),
    63: ("cnpj", 8, None, "BPe"),
    66: ("cnpj", 7, None, "NF3e"),
    62: ("cnpj", 7, None, "NFCom"),
}
PREFIXOS_DE_ID = {"NFe", "CTe", "MDFe", "BPe", "NF3e", "NFCom"}

# AAMM a partir do qual o emitente CPF existe na chave (homologação em 01/08/2018, NT 2018.001 v1.00; antes disso, e no
# leiaute 1.10, em que a posição 35 ainda não era o tpEmis, NT 2013.007, é sempre CNPJ). A NFF começa em 2104 na NF-e e na
# NFC-e (NT 2021.002: homologação em 26/04/2021) e em 2006 no CT-e e no MDF-e (o tpEmis 3 só passou a ser aceito com as
# NTs da NFF: NT MDF-e 2020.002, homologação em 06/2020; NT CT-e 2020.001). Séries do PAA: 970 a 989 (NT 2026.001).
PRIMEIRO_AAMM_COM_CPF = 1808
PRIMEIRO_AAMM_DA_NFF = 2104
PRIMEIRO_AAMM_DA_NFF_DE_TRANSPORTE = 2006

# Códigos numéricos de 8 dígitos proibidos (regra B03-10, rejeição 897, NT 2019.001 v1.70): os 10 repdígitos e as 10
# sequências crescentes. Valem para todos os modelos (só restringem a saída).
CODIGOS_PROIBIDOS = {d * 11_111_111 for d in range(10)} | {
    12_345_678, 23_456_789, 34_567_890, 45_678_901, 56_789_012, 67_890_123, 78_901_234, 89_012_345, 90_123_456, 1_234_567}

# Ocorrências na ordem canônica, fixada pelo formato dos vetores.
OCORRENCIAS = ["NumeroZero", "SequencialZero", "DataNffInvalida", "CodigoProibido", "CodigoIgualAoNumero",
               "EmitentePontoFixo", "DvDoEmitenteInvalido", "Anomalia"]


def estrutura_de_chave_dfe(chave):
    return (len(chave) == 44 and so_digitos(chave[:6]) and all(c in DIGITOS + MAIUSCULAS for c in chave[6:18])
            and so_digitos(chave[18:]))


def classificar_emitente(chave):
    """Devolve (tipo, modelo, nff, exclui_zeros, anomalia): tipo "cpf", "cnpj" ou None; anomalia None, "estrutura",
    "zerada", "modelo" (sem leiaute) ou o motivo da anomalia de classificação (com leiaute)."""
    if not estrutura_de_chave_dfe(chave):
        return None, None, False, False, "estrutura"
    if chave == "0" * 44:
        return None, None, False, False, "zerada"
    modelo = int(chave[20:22])
    if modelo not in MODELOS_DFE:
        return None, None, False, False, "modelo"
    regra = MODELOS_DFE[modelo][0]
    com_letra = any(c in MAIUSCULAS for c in chave[6:18])
    aamm, serie, tipo_emissao_3 = int(chave[2:6]), int(chave[22:25]), chave[34] == "3"
    serie_do_paa = 970 <= serie <= 989
    if regra == "cnpj":
        return "cnpj", modelo, False, False, None
    if regra == "zeros":
        # Letra em 7–18 → CNPJ; senão 7–9 = 000 → CPF; senão CNPJ sem nunca produzir 000 em 7–9.
        nff = tipo_emissao_3 and not serie_do_paa and aamm >= PRIMEIRO_AAMM_DA_NFF_DE_TRANSPORTE
        if com_letra:
            return "cnpj", modelo, nff, False, None
        if chave[6:9] == "000":
            return "cpf", modelo, nff, False, None
        return "cnpj", modelo, nff, True, None
    # NF-e e NFC-e, na ordem: AAMM anterior ao CPF ou letra → CNPJ; tpEmis 3 fora do PAA é a NFF a partir de 2104 (posição
    # 30: 1 CNPJ, 2 CPF) e anomalia antes (o SCAN, tpEmis 3 com séries 900 a 999, foi desativado até 30/09/2014, NT
    # 2013.007 v1.03); depois a série: 000–909 CNPJ (890–899, NFA-e, com o CNPJ da SEFAZ, NT 2018.001 item 03), 910–969
    # CPF (NT 2018.001 item 03.2; no 65, NT 2023.002), e só na NF-e 970–979 CPF e 980–989 CNPJ (PAA, NT 2026.001 v1.02b,
    # regras C02-30 e C02a-10); o resto é anomalia. CPF sem 000 em 7–9 é anomalia.
    nff_possivel = tipo_emissao_3 and not serie_do_paa
    nff = nff_possivel and aamm >= PRIMEIRO_AAMM_DA_NFF
    if com_letra or aamm < PRIMEIRO_AAMM_COM_CPF:
        return "cnpj", modelo, nff, False, None
    if nff_possivel and not nff:
        return None, modelo, False, False, "tpemis-3-fora-do-periodo"
    if nff:
        tipo = {"1": "cnpj", "2": "cpf"}.get(chave[29])
        motivo = "nff-sem-tipo-de-emissor"
    elif serie <= 909:
        tipo = "cnpj"
    elif serie <= 969:
        tipo = "cpf"
    elif regra == "series-com-paa" and serie <= 979:
        tipo = "cpf"
    elif regra == "series-com-paa" and serie <= 989:
        tipo = "cnpj"
    else:
        tipo, motivo = None, "serie-sem-emissao"
    if tipo is None:
        return None, modelo, False, False, motivo
    if tipo == "cpf" and chave[6:9] != "000":
        return None, modelo, False, False, "cpf-sem-zeros"
    return tipo, modelo, nff, False, None


def dias_no_mes(mes, ano):
    if mes == 2:
        return 29 if ano % 4 == 0 else 28
    return 30 if mes in (4, 6, 9, 11) else 31


def mesmo_sem_caixa_ascii(a, b):
    tabela = str.maketrans(MAIUSCULAS, MINUSCULAS)
    return a.translate(tabela) == b.translate(tabela)


class NumeroDfe:
    """Domínio dfe-numero (versão do br.chave-dfe): número e código, com o emitente já pseudonimizado nos ajustes."""

    def __init__(self, chave, dominio, versao, ambiente):
        k = lambda variante: chave_de_dominio(chave, dominio, versao, variante, ambiente)
        self.numero = Feistel(k("nnf"), 10, 10**4, 10**5, nome="nnf")
        # Dia: valor = o próprio dia em 6 × 6, 24 rodadas porque min(a, b) < 10 (Durak e Vaudenay, 2018).
        self.dia = Feistel(k("nff-dia"), 10, 6, 6, rodadas=24, nome="nff-dia")
        self.sequencial = {4: Feistel(k("nff-seq"), 10, 10**2, 10**2, nome="nff-seq"), 5: Feistel(k("nff-seq"), 10, 10**2, 10**3, nome="nff-seq")}
        self.codigo8 = Feistel(k("cnf8"), 10, 10**4, 10**4, nome="cnf8")
        self.codigo7 = Feistel(k("cnf7"), 10, 10**3, 10**4)

    def pseudonimizar(self, chave, nff, tamanho_do_codigo):
        """Devolve (chave, ocorrências); a chave vem e volta como lista de caracteres, com o emitente já trocado."""
        ocorrencias = set()
        emitente = "".join(chave[6:20]).encode("ascii")
        if nff is None:
            # ajuste = emitente' ‖ 0x00 ‖ modelo ‖ série; número 0 fica.
            numero = int("".join(chave[25:34]))
            if numero == 0:
                ocorrencias.add("NumeroZero")
            else:
                ajuste = emitente + b"\x00" + "".join(chave[20:25]).encode("ascii")
                chave[25:34] = f"{self.numero.permutar(numero, ajuste, excluido=lambda v: v == 0):09d}"
        else:
            ocorrencias |= self._partes_da_nff(chave, nff, emitente)
        ajuste = emitente + "".join(chave[25:34]).encode("ascii")
        if tamanho_do_codigo == 7:
            chave[36:43] = f"{self.codigo7.permutar(int(''.join(chave[36:43])), ajuste):07d}"
            return chave, ocorrencias
        # Código proibido antes de código igual ao número' (comparação numérica); depois os dois saem do E.
        codigo, numero = int("".join(chave[35:43])), int("".join(chave[25:34]))
        if codigo in CODIGOS_PROIBIDOS:
            return chave, ocorrencias | {"CodigoProibido"}
        if codigo == numero:
            return chave, ocorrencias | {"CodigoIgualAoNumero"}
        adicional = numero if numero < 10**8 else None
        chave[35:43] = f"{self.codigo8.permutar(codigo, ajuste, excluido=CODIGOS_PROIBIDOS.__contains__, adicional=adicional):08d}"
        return chave, ocorrencias

    def _partes_da_nff(self, chave, nff, emitente):
        """Dia entre os que existem no mês do AAMM e no do número (ano 2000 + AA); sequencial sem o 0."""
        ocorrencias = set()
        ano, mes_do_aamm, mes_do_numero = int("".join(chave[2:4])), int("".join(chave[4:6])), int("".join(chave[nff["mes"]:nff["mes"] + 2]))
        dias = 0
        if 1 <= mes_do_aamm <= 12 and 1 <= mes_do_numero <= 12:
            dias = min(dias_no_mes(mes_do_aamm, 2000 + ano), dias_no_mes(mes_do_numero, 2000 + ano))
        i_dia, i_seq, tamanho = nff["dia"], nff["sequencial"], nff["tamanho"]

        def ajuste(com_dia):
            # emitente' ‖ 0x00 ‖ AAMM ‖ modelo ‖ série ‖ número, com o sequencial (e, sem o dia, o dia) em '0'.
            numero = list(chave[25:34])
            numero[i_seq - 25:i_seq - 25 + tamanho] = "0" * tamanho
            if not com_dia:
                numero[i_dia - 25:i_dia - 25 + 2] = "00"
            return emitente + b"\x00" + "".join(chave[2:6] + chave[20:25] + numero).encode("ascii")

        dia = int("".join(chave[i_dia:i_dia + 2]))
        if 1 <= dia <= dias:
            novo = self.dia.permutar(dia, ajuste(False), excluido=lambda v: v == 0 or v > dias)
            chave[i_dia:i_dia + 2] = f"{novo:02d}"
        else:
            ocorrencias.add("DataNffInvalida")
        sequencial = int("".join(chave[i_seq:i_seq + tamanho]))
        if sequencial == 0:
            ocorrencias.add("SequencialZero")
        else:
            novo = self.sequencial[tamanho].permutar(sequencial, ajuste(True), excluido=lambda v: v == 0)
            chave[i_seq:i_seq + tamanho] = f"{novo:0{tamanho}d}"
        return ocorrencias


class ChaveDfe:
    """Formas: 44 posições; prefixo do Id do modelo (caixa ASCII ignorada, devolvido como veio) e 44 posições; 11 grupos
    de 4 separados por um espaço (DANFE), sem prefixo. Preenchimento U+0020 à direita volta como veio. A chave de 44
    zeros volta como veio. Anomalia de classificação, modelo fora dos aceitos e prefixo de outro modelo seguem
    entradaMalformada; cDV errado segue entradaInvalida (δ mod 10); o DV interno do emitente segue sempre δ mod 100.
    Ordem: emitente, número e código (com o emitente' nos ajustes), cDV."""

    def __init__(self, chave, dominio, versao, ambiente, modelos, invalida, malformada, componentes):
        c = componentes
        # As políticas das colunas não valem dentro da chave: as instâncias só emprestam a permutação.
        self.cpf = Cpf(chave, c["cpf"]["dominio"], c["cpf"]["versao"], ambiente, "falhar", "falhar")
        self.cnpj = Cnpj(chave, c["cnpj"]["dominio"], c["cnpj"]["versao"], ambiente, c["cnpj"]["permutarOrdem"], "falhar", "falhar")
        self.dfe_numero = NumeroDfe(chave, dominio, versao, ambiente)
        self.modelos, self.invalida, self.malformada = set(modelos), invalida, malformada

    def _malformado(self, entrada, ocorrencias):
        saida, desfecho = malformado(self.malformada, entrada)
        return saida, desfecho, ocorrencias

    def texto(self, entrada):
        corpo, preenchimento = tirar_espacos_a_direita(entrada)
        if not corpo:
            return entrada, "Vazio", set()
        if len(corpo) > 54:
            return self._malformado(entrada, set())
        prefixo, em_grupos = "", len(corpo) == 54
        if em_grupos:
            if any(corpo[5 * g - 1] != " " for g in range(1, 11)):
                return self._malformado(entrada, set())
            chave = "".join(corpo[5 * g:5 * g + 4] for g in range(11))
        elif len(corpo) >= 44 and (len(corpo) == 44 or any(mesmo_sem_caixa_ascii(corpo[:-44], p) for p in PREFIXOS_DE_ID)):
            prefixo, chave = corpo[:-44], corpo[-44:]
        else:
            return self._malformado(entrada, set())
        tipo, modelo, nff, exclui_zeros, anomalia = classificar_emitente(chave)
        if anomalia == "zerada":
            return entrada, "Zerado", set()
        ocorrencias = {"Anomalia"} if anomalia is not None and modelo is not None else set()
        if (anomalia is not None or modelo not in self.modelos
                or (prefixo and not mesmo_sem_caixa_ascii(prefixo, MODELOS_DFE[modelo][3]))):
            return self._malformado(entrada, ocorrencias)
        deslocamento = (int(chave[43]) - dv_chave_dfe(chave[:43])) % 10
        if deslocamento and not seguir_com_dv_invalido(self.invalida):
            return None, "AnuladoDvInvalido", set()
        nova = list(chave)
        if tipo == "cpf":
            cpf, ocorrencias = self.cpf.na_chave(chave[9:20])
            nova[9:20] = cpf
        else:
            cnpj, ocorrencias = self.cnpj.na_chave(chave[6:20], exclui_zeros)
            nova[6:20] = cnpj
        _, tamanho_do_codigo, leiaute_nff, _ = MODELOS_DFE[modelo]
        nova, do_numero = self.dfe_numero.pseudonimizar(nova, leiaute_nff if nff else None, tamanho_do_codigo)
        ocorrencias |= do_numero
        nova[43] = str((dv_chave_dfe("".join(nova[:43])) + deslocamento) % 10)
        nova = "".join(nova)
        # Invariante: a saída tem a mesma classificação da entrada.
        if classificar_emitente(nova) != (tipo, modelo, nff, exclui_zeros, None):
            raise Falha("chave de DF-e", "a saída mudou de classificação")
        if nova == chave:
            return entrada, "PontoFixo", ocorrencias
        if em_grupos:
            corpo_da_saida = " ".join(nova[4 * g:4 * g + 4] for g in range(11))
        else:
            corpo_da_saida = prefixo + nova
        return corpo_da_saida + preenchimento, "DvInvalidoPreservado" if deslocamento else "Pseudonimizado", ocorrencias


# ---------------------------------------------------------------------------------------------------------------------
# Leitura estrita e conferência dos vetores.
# ---------------------------------------------------------------------------------------------------------------------

def carregar(texto, onde):
    def objeto(pares):
        resultado = {}
        for nome, valor in pares:
            if nome in resultado:
                raise Falha(onde, "propriedade repetida")
            resultado[nome] = valor
        return resultado

    def recusar(_):
        raise Falha(onde, "número que não é inteiro")

    try:
        return json.loads(texto, object_pairs_hook=objeto, parse_float=recusar, parse_constant=recusar)
    except json.JSONDecodeError:
        raise Falha(onde, "JSON inválido") from None


def ler_arquivo(caminho):
    """UTF-8 sem BOM nem byte inválido, JSON estrito."""
    onde = os.path.basename(caminho)
    with open(caminho, "rb") as arquivo:
        dados = arquivo.read()
    if dados.startswith(b"\xef\xbb\xbf"):
        raise Falha(onde, "BOM no início")
    try:
        return carregar(dados.decode("utf-8"), onde)
    except UnicodeDecodeError:
        raise Falha(onde, "UTF-8 inválido") from None


def exigir(objeto, obrigatorias, opcionais, onde):
    if not isinstance(objeto, dict):
        raise Falha(onde, "não é objeto")
    desconhecidas = set(objeto) - set(obrigatorias) - set(opcionais)
    if desconhecidas:
        raise Falha(onde, "propriedade desconhecida")
    if any(nome not in objeto for nome in obrigatorias):
        raise Falha(onde, "falta uma propriedade obrigatória")


def inteiro(valor, onde):
    if type(valor) is not int:
        raise Falha(onde, "esperado inteiro")
    return valor


def tem_surrogate_sem_par(texto):
    """Em unidades UTF-16."""
    i = 0
    while i < len(texto):
        if "\ud800" <= texto[i] <= "\udbff" and i + 1 < len(texto) and "\udc00" <= texto[i + 1] <= "\udfff":
            i += 2
        elif "\ud800" <= texto[i] <= "\udfff":
            return True
        else:
            i += 1
    return False


def texto_de(valor, onde):
    """Texto JSON válido em Unicode: surrogate sem par só nos campos em UTF-16."""
    if type(valor) is not str:
        raise Falha(onde, "esperado texto")
    if any("\ud800" <= c <= "\udfff" for c in valor):
        raise Falha(onde, "surrogate sem par em texto; use o campo em UTF-16")
    return valor


def lista(valor, onde):
    if not isinstance(valor, list) or not valor:
        raise Falha(onde, "esperada lista não vazia")
    return valor


def hexadecimal(valor, onde):
    texto = texto_de(valor, onde)
    if re.fullmatch(r"(?:[0-9a-f]{2})*", texto, re.ASCII) is None:
        raise Falha(onde, "hexadecimal fora do formato")
    return bytes.fromhex(texto)


POLITICAS = {"preservar", "anular", "falhar"}

# Por transformador e versão: uma versão nova só entra com a implementação dela, nunca conferida com o algoritmo de outra.
PARAMETROS = {
    "br.cpf@1": ["entradaInvalida", "entradaMalformada"],
    "br.cnpj@1": ["permutarOrdem", "entradaInvalida", "entradaMalformada"],
    "br.telefone@1": ["entradaMalformada"],
    "email@1": ["entradaMalformada"],
    "br.cep@1": ["entradaMalformada"],
    "id.permutar@1": ["n", "entradaMalformada"],
    "br.chave-pix@1": ["entradaMalformada", "componentes"],
    "br.chave-dfe@1": ["modelos", "entradaInvalida", "entradaMalformada", "componentes"],
    "texto.substituir@1": ["comprimentoMaximo"],
}

# Componentes dos transformadores compostos: as instâncias das colunas usadas dentro do valor. Na chave de DF-e, as
# políticas das colunas não valem dentro da chave, por isso não aparecem.
COMPONENTES = {
    "br.chave-pix@1": {
        "cpf": ["dominio", "versao", "entradaInvalida"],
        "cnpj": ["dominio", "versao", "permutarOrdem", "entradaInvalida"],
        "email": ["dominio", "versao"],
        "telefone": ["dominio", "versao"],
    },
    "br.chave-dfe@1": {
        "cpf": ["dominio", "versao"],
        "cnpj": ["dominio", "versao", "permutarOrdem"],
    },
}

# Versões implementadas de cada componente: uma versão nova só entra com a implementação dela.
VERSOES_DOS_COMPONENTES = {"cpf": {1}, "cnpj": {1}, "email": {1}, "telefone": {1}}

DESFECHOS = {"Pseudonimizado", "DvInvalidoPreservado", "PontoFixo", "AnuladoDvInvalido", "AnuladoMalformado", "CopiadoEmClaro",
             "ServicoPreservado", "Vazio", "Zerado", "Substituido"}


def politica(objeto, nome, onde):
    if objeto[nome] not in POLITICAS:
        raise Falha(onde, "política desconhecida")
    return objeto[nome]


def logico(valor, onde):
    if type(valor) is not bool:
        raise Falha(onde, "esperado lógico")
    return valor


def montar(transformador, versao, chave, caso, onde):
    dominio, ambiente = texto_de(caso["dominio"], onde), texto_de(caso["ambiente"], onde)
    identidade = f"{transformador}@{versao}"
    if identidade not in PARAMETROS:
        raise Falha(onde, "transformador ou versão sem implementação")
    if identidade == "texto.substituir@1":
        comprimento = caso["comprimentoMaximo"]
        if comprimento is not None and not 1 <= inteiro(comprimento, onde) <= 2**31 - 1:
            raise Falha(onde, "comprimento máximo nem nulo nem de 1 a 2147483647")
        return TextoSubstituir(comprimento)
    malformada = politica(caso, "entradaMalformada", onde)
    if identidade == "br.cpf@1":
        return Cpf(chave, dominio, versao, ambiente, politica(caso, "entradaInvalida", onde), malformada)
    if identidade == "br.cnpj@1":
        return Cnpj(chave, dominio, versao, ambiente, logico(caso["permutarOrdem"], onde), politica(caso, "entradaInvalida", onde), malformada)
    if identidade == "br.telefone@1":
        return Telefone(chave, dominio, versao, ambiente, malformada)
    if identidade == "email@1":
        return Email(chave, dominio, versao, ambiente, malformada)
    if identidade == "br.cep@1":
        return Cep(chave, dominio, versao, ambiente, malformada)
    if identidade == "id.permutar@1":
        return Identificador(chave, dominio, versao, ambiente, inteiro(caso["n"], onde), malformada)
    if identidade not in COMPONENTES:
        raise Falha(onde, "transformador ou versão sem implementação")
    componentes = caso["componentes"]
    exigir(componentes, list(COMPONENTES[identidade]), [], onde)
    for nome, propriedades in COMPONENTES[identidade].items():
        exigir(componentes[nome], propriedades, [], onde)
        texto_de(componentes[nome]["dominio"], onde)
        if inteiro(componentes[nome]["versao"], onde) not in VERSOES_DOS_COMPONENTES[nome]:
            raise Falha(onde, "versão de componente sem implementação")
        if "permutarOrdem" in propriedades:
            logico(componentes[nome]["permutarOrdem"], onde)
        if "entradaInvalida" in propriedades:
            politica(componentes[nome], "entradaInvalida", onde)
    if identidade == "br.chave-pix@1":
        return ChavePix(chave, dominio, versao, ambiente, malformada, componentes)
    # O número e o código de todos os DF-e ficam no domínio dfe-numero; os modelos são os nove tratados, sem repetição.
    if dominio != "dfe-numero":
        raise Falha(onde, "domínio da chave de DF-e diferente de dfe-numero")
    modelos = lista(caso["modelos"], onde)
    if any(type(m) is not int or m not in MODELOS_DFE for m in modelos) or len(set(modelos)) != len(modelos):
        raise Falha(onde, "modelos fora dos nove ou repetidos")
    return ChaveDfe(chave, dominio, versao, ambiente, modelos, politica(caso, "entradaInvalida", onde), malformada, componentes)


def de_utf16(par, campo, onde):
    """Campo em UTF-16 só existe para texto com surrogate sem par."""
    texto = u16_de_hex(texto_de(par[campo], onde), onde)
    if not tem_surrogate_sem_par(texto):
        raise Falha(onde, "campo em UTF-16 com texto válido; use o campo de texto")
    return texto


def executar(transformador, instancia, par, onde):
    """Calcula o resultado do par: ('erro', código) ou (saída, desfecho)."""
    if "entradaUtf16" in par:
        entrada = de_utf16(par, "entradaUtf16", onde)
    else:
        entrada = par["entrada"]
        entrada = inteiro(entrada, onde) if type(entrada) is not str else u16(texto_de(entrada, onde))
    entidade = None
    if transformador == "br.cep":
        entidade = de_utf16(par, "entidadeUtf16", onde) if "entidadeUtf16" in par else u16(texto_de(par["entidade"], onde))
    try:
        if type(entrada) is int:
            if not hasattr(instancia, "numero"):
                raise Falha(onde, "inteiro num transformador de texto")
            return instancia.numero(entrada, entidade) if entidade is not None else instancia.numero(entrada)
        if not hasattr(instancia, "texto"):
            raise Falha(onde, "texto num transformador de inteiros")
        return instancia.texto(entrada, entidade) if entidade is not None else instancia.texto(entrada)
    except Erro as erro:
        return "erro", erro.codigo


def confere(resultado, par, onde):
    if resultado[0] == "erro":
        return "erro" in par and "saida" not in par and par["erro"] == resultado[1]
    if "saida" not in par or "erro" in par or par.get("desfecho") != resultado[1]:
        return False
    if len(resultado) == 3 and par.get("ocorrencias") != [o for o in OCORRENCIAS if o in resultado[2]]:
        return False
    esperado, obtido = par["saida"], resultado[0]
    if esperado is None or obtido is None:
        return esperado is None and obtido is None
    if type(obtido) is int:
        return type(esperado) is int and esperado == obtido
    return type(esperado) is str and u16(texto_de(esperado, onde)) == obtido


# Na chave de DF-e, a permutação em que cada ramo de caminhada precisa caminhar.
PARTE_DA_CAMINHADA = {
    "caminhada-cpf": "cpf", "caminhada-raiz-000": "cnpj-numerica", "caminhada-dia": "nff-dia",
    "caminhada-sequencial": "nff-seq", "caminhada-numero-zero": "nnf", "caminhada-codigo-proibido": "cnf8",
    "caminhada-codigo-numero": "cnf8",
}


def conferir_par(transformador, instancia, par, onde):
    obrigatorias = ["ramo", "origem"]
    opcionais = ["entrada", "entradaUtf16", "saida", "erro", "desfecho"] + (["entidade", "entidadeUtf16"] if transformador == "br.cep" else [])
    if transformador == "br.chave-dfe":
        opcionais.append("ocorrencias")
    exigir(par, obrigatorias, opcionais, onde)
    if transformador == "br.chave-dfe":
        if ("ocorrencias" in par) != ("saida" in par):
            raise Falha(onde, "ocorrências são obrigatórias com saída e proibidas com erro")
        ocorrencias = par.get("ocorrencias", [])
        if (not isinstance(ocorrencias, list) or any(type(o) is not str or o not in OCORRENCIAS for o in ocorrencias)
                or ocorrencias != [o for o in OCORRENCIAS if o in ocorrencias]):
            raise Falha(onde, "ocorrências fora do formato, desconhecidas, repetidas ou fora da ordem canônica")
    if ("entrada" in par) == ("entradaUtf16" in par):
        raise Falha(onde, "precisa de exatamente uma entre entrada e entradaUtf16")
    if transformador == "br.cep" and ("entidade" in par) == ("entidadeUtf16" in par):
        raise Falha(onde, "precisa de exatamente uma entre entidade e entidadeUtf16")
    if ("saida" in par) == ("erro" in par) or ("saida" in par) != ("desfecho" in par):
        raise Falha(onde, "precisa de saída e desfecho, ou só de erro")
    if "desfecho" in par and par["desfecho"] not in DESFECHOS:
        raise Falha(onde, "desfecho desconhecido")
    if "erro" in par and par["erro"] not in ("INC1003", "INC1004"):
        raise Falha(onde, "código de erro desconhecido")
    texto_de(par["ramo"], onde)
    conferir_origem(par["origem"], onde)
    passos_antes = len(PASSOS)
    resultado = executar(transformador, instancia, par, onde)
    if not confere(resultado, par, onde):
        raise Falha(onde, "a saída diverge do vetor")
    # Os ramos de caminhada existem para exercitar o cycle-walking: a entrada precisa ter caminhado de verdade.
    parte = PARTE_DA_CAMINHADA.get(par["ramo"]) if transformador == "br.chave-dfe" else None
    passos = [passo for nome, passo in PASSOS[passos_antes:] if parte is None or nome == parte]
    if par["ramo"].startswith("caminhada") and max(passos, default=0) < 2:
        raise Falha(onde, "ramo de caminhada sem cycle-walking")


def conferir_origem(origem, onde):
    if not isinstance(origem, dict) or origem.get("tipo") not in ("gerador-br", "construido", "inversa", "literal"):
        raise Falha(onde, "origem sem tipo conhecido")
    if origem["tipo"] == "literal":
        exigir(origem, ["tipo"], [], onde)
        return
    exigir(origem, ["tipo", "metodo" if origem["tipo"] == "gerador-br" else "regra", "semente", "formas"], [], onde)
    inteiro(origem["semente"], onde)
    if not isinstance(origem["formas"], list) or any(type(f) is not str for f in origem["formas"]):
        raise Falha(onde, "formas fora do formato")


def conferir_transformador(caminho, documento):
    arquivo = os.path.basename(caminho)
    exigir(documento, ["descricao", "transformador", "versao", "chave", "casos"], [], arquivo)
    transformador = texto_de(documento["transformador"], arquivo)
    versao = inteiro(documento["versao"], arquivo)
    if f"{transformador}@{versao}" not in PARAMETROS or ARQUIVOS.get(arquivo) is not conferir_transformador:
        raise Falha(arquivo, "transformador ou versão sem conferidor")
    if f"{transformador}@{versao}.json" != arquivo:
        raise Falha(arquivo, "nome do arquivo diferente de transformador@versão")
    texto_de(documento["descricao"], arquivo)
    chave = ler_chave(documento["chave"], arquivo)
    if not isinstance(documento["casos"], list) or not documento["casos"]:
        raise Falha(arquivo, "sem casos")
    nomes, pares, ramos = set(), 0, set()
    for caso in documento["casos"]:
        nome = caso.get("nome") if isinstance(caso, dict) else None
        if type(nome) is not str or nome in nomes:
            raise Falha(arquivo, "caso sem nome ou com nome repetido")
        nomes.add(nome)
        onde = f"{arquivo}, caso '{nome}'"
        exigir(caso, ["nome", "dominio", "ambiente", "pares"] + PARAMETROS[f"{transformador}@{versao}"], [], onde)
        instancia = montar(transformador, versao, chave, caso, onde)
        if not isinstance(caso["pares"], list) or not caso["pares"]:
            raise Falha(onde, "sem pares")
        for indice, par in enumerate(caso["pares"]):
            conferir_par(transformador, instancia, par, f"{onde}, par {indice}")
            ramos.add(par["ramo"])
            pares += 1
    faltando = RAMOS_EXIGIDOS.get(arquivo, set()) - ramos
    if faltando:
        raise Falha(arquivo, f"faltam {len(faltando)} ramo(s) crítico(s)")
    return pares


def conferir_derivacao(caminho, documento):
    arquivo = os.path.basename(caminho)
    exigir(documento, ["descricao", "sal", "chaves"], [], arquivo)
    texto_de(documento["descricao"], arquivo)
    if texto_de(documento["sal"], arquivo) != SAL.decode("ascii"):
        raise Falha(arquivo, "sal diferente")
    conferidos = 0
    for chave in lista(documento["chaves"], arquivo):
        exigir(chave, ["nome", "texto", "impressaoDigital", "prk", "feistel", "semente"], [], arquivo)
        onde = f"{arquivo}, chave '{texto_de(chave['nome'], arquivo)}'"
        valor = ler_chave(chave["texto"], onde)
        if hkdf_extrair(SAL, valor) != hexadecimal(chave["prk"], onde) or impressao_digital(valor) != texto_de(chave["impressaoDigital"], onde):
            raise Falha(onde, "PRK ou impressão digital diverge")
        for indice, item in enumerate(lista(chave["feistel"], onde)):
            local = f"{onde}, feistel {indice}"
            exigir(item, ["dominio", "versao", "variante", "ambiente", "subchave"], [], local)
            calculada = chave_de_dominio(valor, texto_de(item["dominio"], local), inteiro(item["versao"], local),
                                         texto_de(item["variante"], local), texto_de(item["ambiente"], local))
            if calculada != hexadecimal(item["subchave"], local):
                raise Falha(local, "subchave diverge")
            conferidos += 1
        for indice, item in enumerate(lista(chave["semente"], onde)):
            local = f"{onde}, semente {indice}"
            exigir(item, ["transformador", "dominio", "versao", "ambiente", "subchave"], [], local)
            calculada = chave_de_semente(valor, texto_de(item["transformador"], local), texto_de(item["dominio"], local),
                                         inteiro(item["versao"], local), texto_de(item["ambiente"], local))
            if calculada != hexadecimal(item["subchave"], local):
                raise Falha(local, "subchave diverge")
            conferidos += 1
    return conferidos


def conferir_feistel(caminho, documento):
    arquivo = os.path.basename(caminho)
    exigir(documento, ["descricao", "rodadas", "chaveDeDominio", "casos"], [], arquivo)
    texto_de(documento["descricao"], arquivo)
    chave = hexadecimal(documento["chaveDeDominio"], arquivo)
    rodadas = inteiro(documento["rodadas"], arquivo)
    conferidos, nomes = 0, set()
    for caso in lista(documento["casos"], arquivo):
        exigir(caso, ["nome", "radix", "a", "b", "ajuste", "excluidos", "pares"], [], arquivo)
        nome = texto_de(caso["nome"], arquivo)
        if nome in nomes:
            raise Falha(arquivo, "caso com nome repetido")
        nomes.add(nome)
        onde = f"{arquivo}, caso '{nome}'"
        feistel = Feistel(chave, inteiro(caso["radix"], onde), inteiro(caso["a"], onde), inteiro(caso["b"], onde), rodadas)
        if not isinstance(caso["excluidos"], list):
            raise Falha(onde, "excluídos fora do formato")
        excluidos = {inteiro(valor, onde) for valor in caso["excluidos"]}
        ajuste = hexadecimal(caso["ajuste"], onde)
        for indice, par in enumerate(lista(caso["pares"], onde)):
            local = f"{onde}, par {indice}"
            exigir(par, ["entrada", "saida"], [], local)
            if feistel.permutar(inteiro(par["entrada"], local), ajuste, excluido=excluidos.__contains__) != inteiro(par["saida"], local):
                raise Falha(local, "a saída diverge do vetor")
            conferidos += 1
    return conferidos


UFS_DO_VOLUME = [11, 12, 13, 14, 15, 16, 17, 21, 22, 23, 24, 25, 26, 27, 28, 29, 31, 32, 33, 35, 41, 42, 43, 50, 51, 52, 53]
MODELOS_DO_VOLUME = [55, 65, 57, 58, 67, 64, 63, 66, 62]
DOMINIOS_DE_EMAIL_DO_VOLUME = ["example.org", "example.com", "example.net"]
NAO_GEOGRAFICOS_DO_VOLUME = ["800", "300", "303", "500", "900"]
DDDS_INEXISTENTES_DO_VOLUME = ["23", "25", "26", "29", "36", "39", "52", "56", "57", "58", "59", "72", "76", "78"]
ACENTOS_DO_VOLUME = "\u00c0\u00c7\u00c9\u00d5\u00de\u00d7\u00df\u00ff"


def volume_telefone(h):
    ddd = f"{1 + h[0] % 9}{1 + h[1] % 9}"
    r = int.from_bytes(h[3:11], "big")
    classe = h[2] % 5
    if classe == 4:
        telefone = "0" + NAO_GEOGRAFICOS_DO_VOLUME[h[11] % 5] + f"{r % 10**7:07d}"
    else:
        if classe == 0:
            numero = "9" + f"{r % 10**8:08d}"
        elif classe == 1:
            numero = str(2 + h[11] % 4) + f"{r % 10**7:07d}"
        elif classe == 2:
            numero = str(6 + h[11] % 4) + f"{r % 10**7:07d}"
        else:
            numero = ("3003", "4004")[h[11] % 2] + f"{r % 10**4:04d}"
            ddd = DDDS_INEXISTENTES_DO_VOLUME[h[0] % 14]
        forma = h[12] % 6
        if classe == 3 and forma == 4:
            forma = 0
        mascarado = f"({ddd}) {numero[:-4]}-{numero[-4:]}"
        telefone = (ddd + numero, mascarado, "+55" + ddd + numero, "0" + ddd + numero, numero, "+55 " + mascarado)[forma]
    return telefone + "#" if h[13] % 32 == 0 else telefone


def volume_cep(h):
    valor = int.from_bytes(h[:8], "big") % 10**8
    cep = f"{valor:08d}"
    forma = h[8] % 4
    texto = (cep, f"{cep[:5]}-{cep[5:]}", f"{cep[:2]}.{cep[2:5]}-{cep[5:]}", str(valor))[forma]
    return texto + "  " if h[13] % 16 == 0 else texto


def volume_email(h):
    local = h[:6].hex()
    variante = h[6] % 4
    if variante in (1, 3):
        local = local.upper()
    if variante in (2, 3):
        local = ACENTOS_DO_VOLUME[h[9] % 8] + local
    dominio = DOMINIOS_DE_EMAIL_DO_VOLUME[h[7] % 3]
    if h[8] % 2 == 1:
        dominio = dominio.upper()
    if h[10] % 16 == 0:
        return local + "@" + dominio + "  "
    if h[10] % 16 == 1:
        return local + dominio
    return local + "@" + dominio


def volume_identificador(h, n):
    return int.from_bytes(h[:8], "big") % (n + n // 5) - n // 10


def raiz_baixa_com_dv_certo(emitente):
    """Emitente que, lido como CNPJ, tem raiz numérica começando com 00, ordem diferente de 0000 e DV certo."""
    return (len(emitente) == 14 and emitente[:8].isdigit() and emitente[:8].isascii() and emitente.startswith("00")
            and emitente[8:12] != "0000" and all(c in BASE36 for c in emitente[8:12])
            and emitente[12:].isdigit() and emitente[12:].isascii() and dv_cnpj(emitente[:12]) == int(emitente[12:]))


def volume_chave_dfe(h):
    tipo = h[4] % 3
    if tipo == 0:
        base = "".join(DIGITOS[h[5 + k] % 10] for k in range(12))
        emitente = f"{base}{dv_cnpj(base):02d}"
    elif tipo == 1:
        base = "".join(DIGITOS[h[5 + k] % 10] for k in range(9))
        emitente = f"000{base}{dv_cpf(base):02d}"
    else:
        base = "".join(BASE36[h[5 + k] % 36] for k in range(12))
        emitente = f"{base}{dv_cnpj(base):02d}"
    if raiz_baixa_com_dv_certo(emitente):
        emitente = emitente[:13] + str((int(emitente[13]) + 1) % 10)
    modelo = MODELOS_DO_VOLUME[h[3] % 9]
    chave = (f"{UFS_DO_VOLUME[h[0] % 27]:02d}{18 + h[1] % 9:02d}{1 + h[2] % 12:02d}{emitente}{modelo:02d}"
             f"{int.from_bytes(h[17:19], 'big') % 1000:03d}{int.from_bytes(h[19:23], 'big') % 10**9:09d}"
             f"{'3' if h[23] % 4 == 0 else '1'}{int.from_bytes(h[24:28], 'big') % 10**8:08d}")
    cdv = dv_chave_dfe(chave)
    chave += str((cdv + 1 + h[30] % 9) % 10 if h[29] % 8 == 0 else cdv)
    prefixo = MODELOS_DFE[modelo][3]
    forma = h[28] % 4
    if forma == 0:
        return chave
    if forma == 1:
        return prefixo + chave
    if forma == 2:
        return prefixo.lower() + chave
    return " ".join(chave[4 * g:4 * g + 4] for g in range(11))


def conferir_volume(caminho, documento):
    """Resumo SHA-256 das saídas de valores cujas entradas saem de um contador. A regra de CPF e CNPJ está na descrição do
    arquivo; a dos outros transformadores, no campo entradas de cada caso."""
    arquivo = os.path.basename(caminho)
    exigir(documento, ["descricao", "chave", "casos"], [], arquivo)
    texto_de(documento["descricao"], arquivo)
    chave = ler_chave(documento["chave"], arquivo)
    conferidos, nomes = 0, set()
    for caso in lista(documento["casos"], arquivo):
        exigir(caso, ["nome", "transformador", "versao", "dominio", "ambiente", "quantidade", "resumo"],
               ["permutarOrdem", "n", "entradaMalformada", "entradas"], arquivo)
        nome = texto_de(caso["nome"], arquivo)
        if nome in nomes:
            raise Falha(arquivo, "caso com nome repetido")
        nomes.add(nome)
        onde = f"{arquivo}, caso '{nome}'"
        identidade = f"{texto_de(caso['transformador'], onde)}@{inteiro(caso['versao'], onde)}"
        dominio, ambiente = texto_de(caso["dominio"], onde), texto_de(caso["ambiente"], onde)
        quantidade = inteiro(caso["quantidade"], onde)
        opcoes = set(caso) & {"permutarOrdem", "n", "entradaMalformada", "entradas"}
        if identidade in ("br.cpf@1", "br.cnpj@1"):
            if opcoes != ({"permutarOrdem"} if identidade == "br.cnpj@1" else set()):
                raise Falha(onde, "opções fora das do transformador")
        else:
            esperadas = {"entradaMalformada", "entradas"} | ({"n"} if identidade == "id.permutar@1" else set())
            if opcoes != esperadas or caso["entradaMalformada"] != "preservar":
                raise Falha(onde, "opções fora das do transformador")
            texto_de(caso["entradas"], onde)
        if identidade == "br.cpf@1":
            instancia = Cpf(chave, dominio, 1, ambiente, "preservar", "falhar")
        elif identidade == "br.cnpj@1":
            instancia = Cnpj(chave, dominio, 1, ambiente, logico(caso["permutarOrdem"], onde), "preservar", "falhar")
        elif identidade == "br.telefone@1":
            instancia = Telefone(chave, dominio, 1, ambiente, "preservar")
        elif identidade == "id.permutar@1":
            instancia = Identificador(chave, dominio, 1, ambiente, inteiro(caso["n"], onde), "preservar")
        elif identidade == "br.cep@1":
            instancia = Cep(chave, dominio, 1, ambiente, "preservar")
        elif identidade == "email@1":
            instancia = Email(chave, dominio, 1, ambiente, "preservar")
        elif identidade == "br.chave-dfe@1" and dominio == "dfe-numero":
            componentes = {"cpf": {"dominio": "cpf", "versao": 1}, "cnpj": {"dominio": "cnpj", "versao": 1, "permutarOrdem": False}}
            instancia = ChaveDfe(chave, dominio, 1, ambiente, list(MODELOS_DO_VOLUME), "preservar", "preservar", componentes)
        else:
            raise Falha(onde, "transformador fora do volume")
        if quantidade < 1:
            raise Falha(onde, "quantidade menor que 1")
        resumo = hashlib.sha256()
        for i in range(quantidade):
            h = hashlib.sha256(nome.encode("utf-8") + b"\x00" + i.to_bytes(4, "big")).digest()
            if isinstance(instancia, (Cpf, Cnpj)):
                if isinstance(instancia, Cpf):
                    base = f"{int.from_bytes(h[:8], 'big') % 10**9:09d}"
                    dv = dv_cpf(base)
                else:
                    alfabeto = BASE36 if h[10] % 2 == 1 else DIGITOS
                    base = "".join(alfabeto[h[11 + k] % len(alfabeto)] for k in range(12))
                    dv = dv_cnpj(base)
                if h[8] % 4 == 0:
                    dv = (dv + 1 + h[9] % 99) % 100
                saida, desfecho = instancia.texto(f"{base}{dv:02d}")
                linha = f"{'' if saida is None else saida} {desfecho}\n"
            elif isinstance(instancia, Telefone):
                saida, desfecho = instancia.texto(u16(volume_telefone(h)))
                linha = f"{'' if saida is None else saida} {desfecho}\n"
            elif isinstance(instancia, Identificador):
                saida, desfecho = instancia.numero(volume_identificador(h, instancia.n))
                linha = f"{'' if saida is None else saida} {desfecho}\n"
            elif isinstance(instancia, Cep):
                saida, desfecho = instancia.texto(u16(volume_cep(h)), u16("pessoa-" + h[9:13].hex()))
                linha = f"{'' if saida is None else saida} {desfecho}\n"
            elif isinstance(instancia, Email):
                saida, desfecho = instancia.texto(u16(volume_email(h)))
                linha = f"{'' if saida is None else saida} {desfecho}\n"
            else:
                saida, desfecho, ocorrencias = instancia.texto(u16(volume_chave_dfe(h)))
                if not set(ocorrencias) <= set(OCORRENCIAS):
                    raise Falha(onde, "ocorrência desconhecida")
                linha = f"{'' if saida is None else saida} {desfecho} {'|'.join(o for o in OCORRENCIAS if o in ocorrencias)}\n"
            resumo.update(linha.encode("utf-8"))
        if resumo.hexdigest() != texto_de(caso["resumo"], onde):
            raise Falha(onde, "o resumo diverge do vetor")
        conferidos += quantidade
    return conferidos


# Todo arquivo de testes/vetores/ tem conferidor, e todo conferidor tem arquivo.
ARQUIVOS = {
    "derivacao@1.json": conferir_derivacao,
    "feistel@1.json": conferir_feistel,
    "br.cpf@1.json": conferir_transformador,
    "br.cnpj@1.json": conferir_transformador,
    "br.telefone@1.json": conferir_transformador,
    "email@1.json": conferir_transformador,
    "br.cep@1.json": conferir_transformador,
    "id.permutar@1.json": conferir_transformador,
    "br.chave-pix@1.json": conferir_transformador,
    "br.chave-dfe@1.json": conferir_transformador,
    "texto.substituir@1.json": conferir_transformador,
    "volume@1.json": conferir_volume,
}

# Ramos críticos, escritos a partir das regras de cada transformador: cada arquivo tem de ter ao menos estes (os demais
# ficam presos pelo teste em C# contra as receitas e pela guarda de crescimento).
RAMOS_EXIGIDOS = {
    "br.cpf@1.json": {
        "caminhada", "repdigito", "repdigito-dv-invalido", "zerado", "dv-invalido", "dv-deslocado-em-1",
        "sem-zeros-1", "sem-zeros-2", "numero", "numero-repdigito", "digito-nao-ascii", "sufixo-nao-espaco",
        "vazio"
    },
    "br.cnpj@1.json": {
        "caminhada-num", "caminhada-alfa", "caminhada-alfa-raiz-zero", "alvo-so-com-digitos-e-a", "raiz-zero",
        "raiz-quase-zero", "zerado", "raiz-alfanumerica", "ordem-alfanumerica", "minuscula", "letra-nao-ascii",
        "sem-zeros-1", "numero"
    },
    "br.telefone@1.json": {
        "caminhada-fixo", "borda-servico", "servico-fixo", "servico-nao-geografico", "celular-antigo",
        "sem-ddd", "tronco", "ddd-55", "tamanho-maximo", "tamanho-acima-do-maximo", "malformado-barra",
        "numero-servico", "digito-nao-ascii", "servico-0303", "servico-antigo", "numero-servico-0303",
        "numero-servico-antigo", "numero-900-antigo-e-celular", "zerado", "zerado-formatado", "zerado-assinante",
        "zerado-acima-do-maximo", "quase-zerado", "numero-zerado", "numero-quase-zerado"
    },
    "email@1.json": {
        "espacos-nas-pontas", "fora-da-regra", "latin1", "composicao", "par-substituto", "parte-local-64",
        "parte-local-65", "endereco-254", "endereco-255", "surrogate-sem-par", "controle", "controle-c1",
        "preenchimento-maior-que-excesso"
    },
    "br.cep@1.json": {
        "uf-ac", "uf-al", "uf-am", "uf-ap", "uf-ba", "uf-ce", "uf-df", "uf-es", "uf-go", "uf-ma", "uf-mg",
        "uf-ms", "uf-mt", "uf-pa", "uf-pb", "uf-pe", "uf-pi", "uf-pr", "uf-rj", "uf-rn", "uf-ro", "uf-rr",
        "uf-rs", "uf-sc", "uf-se", "uf-sp", "uf-to", "primeira-faixa", "segunda-faixa", "fronteira",
        "ro-antiga", "entidade-vazia", "entidade-aparada", "entidade-surrogate-sem-par", "sete-digitos",
        "fora-das-faixas", "zerado", "quase-zerado", "numero-zerado"
    },
    "id.permutar@1.json": {
        "caminhada", "extremo", "zero-ou-negativo", "a-partir-de-n"
    },
    "texto.substituir@1.json": {
        "substituido", "cortado", "tabulacao", "vazio", "so-espacos"
    },
    "br.chave-pix@1.json": {
        "cpf", "cnpj", "email", "telefone", "aleatoria", "aleatoria-assimetrica", "aleatoria-maiusculas",
        "cnpj-minusculas", "cnpj-minusculas-dv-errado", "onze-digitos-e-cpf", "arroba-vence", "telefone-zerado"
    },
    "br.chave-dfe@1.json": {
        "aamm-antes-do-cpf", "aamm-primeiro-com-cpf", "anomalia-com-cdv-errado", "anomalia-com-modelo-fora-da-coluna",
        "anomalia-com-prefixo-de-outro-modelo", "caminhada-codigo-numero", "caminhada-codigo-proibido",
        "caminhada-cpf", "caminhada-dia", "caminhada-numero-zero", "caminhada-raiz-000", "caminhada-sequencial",
        "cdv-errado", "chave-da-nfse", "cnpj-alfanumerico", "cnpj-filial", "cnpj-ordem-alfanumerica", "cnpj-raiz-000",
        "cnpj-raiz-e-ordem-alfanumericas", "cnpj-raiz-zero", "codigo-7-com-cara-de-proibido",
        "codigo-igual-ao-numero", "codigo-igual-ao-numero-de-entrada", "codigo-proibido",
        "codigo-proibido-antes-do-igual", "codigo-proibido-cada", "codigo-proibido-com-zero", "cpf-com-cinco-zeros",
        "cpf-com-zeros", "cpf-repdigito", "cpf-sem-zeros", "dv-emitente-invalido", "espaco-a-esquerda", "grupos",
        "grupos-com-espaco-duplo", "grupos-com-outro-separador", "grupos-com-prefixo", "letra-fora-do-emitente",
        "letra-vence-nff", "letra-vence-serie", "letra-vence-tpemis3", "minuscula-no-emitente", "modelo",
        "modelo-fora-da-coluna", "modelo-fora-da-coluna-com-cdv-errado", "modelo-fora-da-tabela", "nff-ano-do-aamm",
        "nff-cnpj", "nff-cnpj-com-zeros-do-cpf", "nff-cpf", "nff-cpf-sem-zeros", "nff-dia-depois-do-ultimo",
        "nff-dia-zero", "nff-mes-diferente", "nff-mes-do-aamm-invalido", "nff-mes-do-numero-invalido",
        "nff-numero-zero", "nff-sem-tipo", "nff-sequencial-zero", "nff-transporte", "nff-ultimo-dia", "nsiteautoriz",
        "numero-pseudonimizado-abaixo-de-10-8", "numero-pseudonimizado-acima-de-10-8", "numero-zero", "outros-tpemis",
        "paa-transporte", "ponto-fixo", "preenchimento", "prefixo", "prefixo-de-outro-modelo",
        "prefixo-de-outro-modelo-com-cdv-errado", "prefixo-desconhecido", "prefixo-em-outra-caixa", "saida-raiz-00",
        "scan", "serie-cnpj", "serie-cpf", "serie-paa-cnpj", "serie-paa-cpf", "serie-sem-emissao", "so-cnpj-raiz-000",
        "tamanho", "tpemis3-fora-do-paa", "tpemis3-fora-do-periodo", "tpemis3-paa", "transporte-antes-da-nff",
        "vazio", "zerada", "zerada-com-prefixo-desconhecido", "zeros-alfanumerico-000", "zeros-cnpj", "zeros-cpf",
        "zeros-cpf-repdigito", "zeros-quase-cpf", "zeros-raiz-000-com-letra", "zeros-raiz-000-e-cpf",
        "zeros-raiz-zero"
    },
}


# ---------------------------------------------------------------------------------------------------------------------
# Âncoras independentes (RFC 4231, RFC 5869 e exemplos didáticos da Receita).
# ---------------------------------------------------------------------------------------------------------------------

def conferir_ancoras():
    conferidas = 0
    documento = ler_arquivo(os.path.join(PASTA_ANCORAS, "rfc4231-hmac-sha256.json"))
    exigir(documento, ["origem", "casos"], [], "rfc4231")
    for caso in lista(documento["casos"], "rfc4231"):
        exigir(caso, ["nome", "chave", "dados", "bytes", "hmac"], [], "rfc4231")
        onde = f"rfc4231, {texto_de(caso['nome'], 'rfc4231')}"
        calculado = hmac.digest(hexadecimal(caso["chave"], onde), hexadecimal(caso["dados"], onde), "sha256")[:inteiro(caso["bytes"], onde)]
        if calculado != hexadecimal(caso["hmac"], onde):
            raise Falha(onde, "HMAC diverge")
        conferidas += 1
    documento = ler_arquivo(os.path.join(PASTA_ANCORAS, "rfc5869-hkdf-sha256.json"))
    exigir(documento, ["origem", "casos"], [], "rfc5869")
    for caso in lista(documento["casos"], "rfc5869"):
        exigir(caso, ["nome", "ikm", "sal", "info", "tamanho", "prk", "okm"], [], "rfc5869")
        onde = f"rfc5869, {texto_de(caso['nome'], 'rfc5869')}"
        prk = hkdf_extrair(hexadecimal(caso["sal"], onde), hexadecimal(caso["ikm"], onde))
        okm = hkdf_expandir(prk, hexadecimal(caso["info"], onde), inteiro(caso["tamanho"], onde))
        if prk != hexadecimal(caso["prk"], onde) or okm != hexadecimal(caso["okm"], onde):
            raise Falha(onde, "HKDF diverge")
        conferidas += 1
    documento = ler_arquivo(os.path.join(PASTA_ANCORAS, "cnpj-alfanumerico-rfb.json"))
    exigir(documento, ["origem", "descricao", "casos"], [], "cnpj-alfanumerico-rfb")
    for caso in lista(documento["casos"], "cnpj-alfanumerico-rfb"):
        exigir(caso, ["nome", "cnpj", "valido"], [], "cnpj-alfanumerico-rfb")
        onde = f"cnpj-alfanumerico-rfb, {texto_de(caso['nome'], 'cnpj-alfanumerico-rfb')}"
        if cnpj_valido(texto_de(caso["cnpj"], onde)) != logico(caso["valido"], onde):
            raise Falha(onde, "DV diverge")
        conferidas += 1
    return conferidas


# ---------------------------------------------------------------------------------------------------------------------
# Guarda: vetor só cresce. Casos novos entram; os existentes nunca mudam.
# ---------------------------------------------------------------------------------------------------------------------

def git(*argumentos):
    resultado = subprocess.run(["git", "-C", RAIZ, *argumentos], capture_output=True)
    if resultado.returncode != 0:
        mensagem = resultado.stderr.decode("utf-8", "replace").strip().splitlines()
        raise Falha("git", f"'{argumentos[0]}' falhou: {mensagem[-1] if mensagem else 'sem mensagem'}")
    return resultado.stdout


def arquivos_da_base(base):
    """Os vetores da base; vazio se a base ainda não tem a pasta (como a main antes do primeiro release com vetores)."""
    if re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._/^~@{}-]*", base, re.ASCII) is None:
        raise Falha("--base", "referência fora do formato")
    if subprocess.run(["git", "-C", RAIZ, "rev-parse", "--verify", "--quiet", base + "^{commit}"], capture_output=True).returncode != 0:
        raise Falha("--base", "referência inexistente no repositório")
    saida = git("ls-tree", "-z", "--name-only", base, "--", "testes/vetores/")
    return sorted(caminho.decode("utf-8").removeprefix("testes/vetores/") for caminho in saida.split(b"\0") if caminho)


def canonico(valor):
    """Comparação que não confunde true com 1 nem depende da ordem das propriedades."""
    return json.dumps(valor, sort_keys=True, ensure_ascii=False)


def comparar_crescimento(nome, antigo, atual, base):
    if not isinstance(antigo, dict) or not isinstance(atual, dict):
        raise Falha(nome, "não é objeto")
    casos_antigos, casos_atuais = antigo.get("casos"), atual.get("casos")
    if not isinstance(casos_antigos, list) or not isinstance(casos_atuais, list):
        if canonico(antigo) != canonico(atual):
            raise Falha(nome, f"mudou em relação a {base}")
        return
    if canonico({k: v for k, v in antigo.items() if k != "casos"}) != canonico({k: v for k, v in atual.items() if k != "casos"}):
        raise Falha(nome, f"cabeçalho mudou em relação a {base}")
    atuais = {}
    for caso in casos_atuais:
        nome_do_caso = caso.get("nome") if isinstance(caso, dict) else None
        if type(nome_do_caso) is not str or nome_do_caso in atuais:
            raise Falha(nome, "caso sem nome ou com nome repetido")
        atuais[nome_do_caso] = caso
    for caso in casos_antigos:
        nome_do_caso = caso.get("nome") if isinstance(caso, dict) else None
        if nome_do_caso not in atuais or canonico(atuais[nome_do_caso]) != canonico(caso):
            raise Falha(f"{nome}, caso '{nome_do_caso}'", f"mudou ou sumiu em relação a {base}")


def conferir_crescimento(base):
    """Os arquivos de vetores da base continuam presentes; os casos deles continuam idênticos; só entram casos novos."""
    nomes = arquivos_da_base(base)
    for nome in nomes:
        antigo = carregar(git("show", f"{base}:testes/vetores/{nome}").decode("utf-8"), f"{nome} em {base}")
        caminho = os.path.join(PASTA_VETORES, nome)
        if not os.path.exists(caminho):
            raise Falha(nome, f"existe em {base} e foi apagado")
        comparar_crescimento(nome, antigo, ler_arquivo(caminho), base)
    return len(nomes)


# ---------------------------------------------------------------------------------------------------------------------
# Autoteste: alterações em memória têm de ser acusadas pelo motivo certo, em todo arquivo e na guarda.
# ---------------------------------------------------------------------------------------------------------------------

def deve_acusar(rotulo, acao, motivo):
    try:
        acao()
    except Falha as falha:
        if motivo in str(falha):
            return
        raise Falha("autoteste", f"{rotulo}: acusado por outro motivo") from None
    raise Falha("autoteste", f"{rotulo}: alteração não acusada")


def primeiro_par(documento, condicao):
    for caso in documento["casos"]:
        for par in caso["pares"]:
            if condicao(par):
                return par
    raise Falha("autoteste", "nenhum par com a forma pedida")


def _com_ocorrencias(documento):
    return primeiro_par(documento, lambda p: len(p.get("ocorrencias", [])) >= 2)


def _com_erro(documento):
    return primeiro_par(documento, lambda p: "erro" in p)


# Alterações da leitura estrita da chave de DF-e e o motivo que cada uma precisa dar.
AUTOTESTE_DA_CHAVE_DFE = [
    ("ocorrências fora da ordem", lambda d: _com_ocorrencias(d).update(ocorrencias=_com_ocorrencias(d)["ocorrencias"][::-1]), "ordem canônica"),
    ("ocorrência desconhecida", lambda d: _com_ocorrencias(d).update(ocorrencias=["Outra"]), "ordem canônica"),
    ("ocorrências com erro", lambda d: _com_erro(d).update(ocorrencias=[]), "proibidas com erro"),
    ("domínio", lambda d: d["casos"][0].update(dominio="dfe-outro"), "dfe-numero"),
    ("modelo repetido", lambda d: d["casos"][0]["modelos"].append(d["casos"][0]["modelos"][0]), "modelos fora"),
    ("modelo fora dos nove", lambda d: d["casos"][0]["modelos"].append(59), "modelos fora"),
    ("versão de componente", lambda d: d["casos"][0]["componentes"]["cpf"].update(versao=2), "versão de componente"),
]


def autoteste():
    for arquivo, conferidor in ARQUIVOS.items():
        caminho = os.path.join(PASTA_VETORES, arquivo)
        if conferidor is conferir_transformador:
            documento = ler_arquivo(caminho)
            par = primeiro_par(documento, lambda p: "saida" in p and p["saida"] is not None)
            par["saida"] = par["saida"] + 1 if type(par["saida"]) is int else par["saida"] + "0"
            deve_acusar(f"{arquivo}, saída", lambda: conferir_transformador(caminho, documento), "diverge")
            documento = ler_arquivo(caminho)
            par = primeiro_par(documento, lambda p: "desfecho" in p)
            par["desfecho"] = "PontoFixo" if par["desfecho"] != "PontoFixo" else "Pseudonimizado"
            deve_acusar(f"{arquivo}, desfecho", lambda: conferir_transformador(caminho, documento), "diverge")
            documento = ler_arquivo(caminho)
            if arquivo == "texto.substituir@1.json":
                # Sem erro possível: o comprimento máximo trocado num caso é que tem de ser acusado.
                caso = next(c for c in documento["casos"] if c["comprimentoMaximo"] is not None and c["comprimentoMaximo"] < 30)
                caso["comprimentoMaximo"] += 1
                deve_acusar(f"{arquivo}, comprimento máximo", lambda: conferir_transformador(caminho, documento), "diverge")
                for recusado, motivo in ((0, "nem nulo"), (-1, "nem nulo"), (2**31, "nem nulo"), (True, "esperado inteiro"), ("30", "esperado inteiro")):
                    documento = ler_arquivo(caminho)
                    documento["casos"][0]["comprimentoMaximo"] = recusado
                    deve_acusar(f"{arquivo}, comprimento máximo recusado", lambda: conferir_transformador(caminho, documento), motivo)
            else:
                par = primeiro_par(documento, lambda p: "erro" in p)
                par["erro"] = "INC1004" if par["erro"] == "INC1003" else "INC1003"
                deve_acusar(f"{arquivo}, erro", lambda: conferir_transformador(caminho, documento), "diverge")
            if arquivo == "br.chave-dfe@1.json":
                for condicao in (lambda p: p.get("ocorrencias") == [], lambda p: bool(p.get("ocorrencias"))):
                    documento = ler_arquivo(caminho)
                    par = primeiro_par(documento, condicao)
                    par["ocorrencias"] = par["ocorrencias"][1:] if par["ocorrencias"] else ["Anomalia"]
                    deve_acusar(f"{arquivo}, ocorrências", lambda: conferir_transformador(caminho, documento), "diverge")
                for rotulo, alterar, motivo in AUTOTESTE_DA_CHAVE_DFE:
                    documento = ler_arquivo(caminho)
                    alterar(documento)
                    deve_acusar(f"{arquivo}, {rotulo}", lambda: conferir_transformador(caminho, documento), motivo)
        elif conferidor is conferir_volume:
            for indice in range(len(ler_arquivo(caminho)["casos"])):
                documento = ler_arquivo(caminho)
                caso = documento["casos"][indice]
                caso["resumo"] = caso["resumo"][:-1] + ("0" if caso["resumo"][-1] != "0" else "1")
                deve_acusar(f"{arquivo}, resumo do caso {indice}", lambda: conferir_volume(caminho, documento), "diverge")
                if "entradaMalformada" in caso:
                    documento = ler_arquivo(caminho)
                    documento["casos"][indice]["entradaMalformada"] = "anular"
                    deve_acusar(f"{arquivo}, política do caso {indice}", lambda: conferir_volume(caminho, documento), "opções fora")
            # As recusas das opções e do transformador, cada uma num documento só com o caso alterado.
            recusas = [
                ("br.cpf", lambda c: c.update(permutarOrdem=True), "opções fora"),
                ("br.cpf", lambda c: c.update(entradas="x"), "opções fora"),
                ("br.telefone", lambda c: c.update(permutarOrdem=False), "opções fora"),
                ("id.permutar", lambda c: c.pop("n"), "opções fora"),
                ("br.cep", lambda c: c.update(transformador="br.chave-pix"), "transformador fora do volume"),
                ("email", lambda c: c.update(quantidade=0), "quantidade menor que 1"),
            ]
            for transformador, alterar, motivo in recusas:
                documento = ler_arquivo(caminho)
                caso = next(c for c in documento["casos"] if c["transformador"] == transformador)
                documento["casos"] = [caso]
                alterar(caso)
                deve_acusar(f"{arquivo}, recusa em {transformador}", lambda: conferir_volume(caminho, documento), motivo)
        elif conferidor is conferir_feistel:
            documento = ler_arquivo(caminho)
            documento["casos"][0]["pares"][0]["saida"] += 1
            deve_acusar(f"{arquivo}, saída", lambda: conferir_feistel(caminho, documento), "diverge")
        else:
            documento = ler_arquivo(caminho)
            item = documento["chaves"][0]["feistel"][0]
            item["subchave"] = item["subchave"][:-2] + ("00" if item["subchave"][-2:] != "00" else "01")
            deve_acusar(f"{arquivo}, subchave", lambda: conferir_derivacao(caminho, documento), "diverge")
        original = ler_arquivo(caminho)
        alterado = ler_arquivo(caminho)
        if "casos" not in alterado:
            alterado["sal"] += "x"
        elif "pares" in alterado["casos"][0]:
            par = alterado["casos"][0]["pares"][0]
            par["saida"] = par.get("saida") is None
        else:
            alterado["casos"][0]["quantidade"] += 1
        deve_acusar(f"{arquivo}, guarda", lambda: comparar_crescimento(arquivo, original, alterado, "a base"), "mudou")


def main(argumentos):
    global RAIZ, PASTA_VETORES, PASTA_ANCORAS
    sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)
    sys.stderr.reconfigure(encoding="utf-8")
    analisador = argparse.ArgumentParser(prog="conferir_vetores.py", allow_abbrev=False,
                                         description="Referência independente dos vetores fixos e guarda de crescimento.")
    analisador.add_argument("--base", metavar="REF", help="exige que os vetores de REF continuem iguais (só casos novos)")
    analisador.add_argument("--raiz", metavar="DIR", help="raiz do repositório (padrão: a do próprio script)")
    analisador.add_argument("--so-crescimento", action="store_true", help="só a guarda de --base, sem conferir os vetores")
    analisador.add_argument("--autoteste", action="store_true", help="prova que alterações são acusadas")
    opcoes = analisador.parse_args(argumentos)
    if opcoes.so_crescimento and (opcoes.base is None or opcoes.autoteste):
        analisador.error("--so-crescimento exige --base e não combina com --autoteste")
    if opcoes.raiz is not None:
        RAIZ = os.path.abspath(opcoes.raiz)
        PASTA_VETORES = os.path.join(RAIZ, "testes", "vetores")
        PASTA_ANCORAS = os.path.join(RAIZ, "testes", "ancoras")
    print(f"Python {sys.version.split()[0]}")
    if not opcoes.so_crescimento:
        print(f"âncoras: {conferir_ancoras()} conferidas")
        presentes = sorted(os.listdir(PASTA_VETORES))
        if presentes != sorted(ARQUIVOS):
            raise Falha("testes/vetores", "arquivos diferentes dos que têm conferidor")
        total = 0
        for nome in sorted(ARQUIVOS):
            caminho = os.path.join(PASTA_VETORES, nome)
            conferidos = ARQUIVOS[nome](caminho, ler_arquivo(caminho))
            if conferidos == 0:
                raise Falha(nome, "nada conferido")
            print(f"{nome}: {conferidos} conferidos")
            total += conferidos
        print(f"vetores: {total} conferidos em {len(ARQUIVOS)} arquivos")
    if opcoes.base is not None:
        print(f"crescimento: {conferir_crescimento(opcoes.base)} arquivos de {opcoes.base} conferidos")
    if opcoes.autoteste:
        autoteste()
        print("autoteste: alterações acusadas")


if __name__ == "__main__":
    try:
        main(sys.argv[1:])
    except Falha as falha:
        print(f"FALHA: {falha}", file=sys.stderr)
        sys.exit(1)
    except Exception as erro:  # noqa: BLE001 - só o tipo: a mensagem poderia trazer um valor
        print(f"FALHA: erro inesperado ({type(erro).__name__}).", file=sys.stderr)
        sys.exit(1)
